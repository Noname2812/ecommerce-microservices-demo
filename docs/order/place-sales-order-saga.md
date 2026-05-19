# Place Sales Order Saga (TASK-08)

Refactor of the Sales flow (`PlaceSalesOrderSagaStateMachine`). The previous design relied on
event-based Promotion redeem + Coupon claim round-trips. The new design moves both flash sale stock
and coupon lock into Redis Lua atomic scripts, and validates sale eligibility with a direct HTTP
call to the Promotion service.

## Flow

```
Handler (sync, <20ms)
  [1] FluentValidation
  [2] Pending limit check (Redis SET NX)         — Sales = max 3 pending/user
  [3] Flash sale gate (Redis Lua DECRBY)         — 409 SoldOut if insufficient
  [4] Publish PlaceSalesOrderRequestedV1
  [5] Return 202 { ticketId }

Saga (orchestrator)
  Initial:
    [1] Validate catalog (variants, prices)      — ICatalogServiceClient (TASK-05 Polly)
    [2] Validate sale eligibility                — ISaleEligibilityService HTTP
    [3] Lock coupon (Redis Lua, atomic)          — ICouponLockService (if CouponCode set)
    [4] Server-side price recalc (1% tolerance)  — PriceMismatchTolerance from PlaceOrderOptions
    [5] Persist Order (status = PROCESSING)      — OrderFactory.BuildSalesFromSaga
    → publish ReserveInventoryRequestedV1 → InventoryReserving

  InventoryReserving:
    InventoryReserved   → PaymentSessionCreating
    InventoryReserveFailed / timeout → Compensating

  PaymentSessionCreating (WhenEnter → CreatePaymentSessionV1):
    PaymentSessionCreated → MarkReadyForPayment → Schedule PaymentExpiry → PaymentPending
    timeout               → Compensating

  PaymentPending:
    PaymentCompleted → MarkPaid + ConfirmCouponUse + ConfirmInventory + OrderConfirmed → Finalize
    PaymentExpiry    → Compensating
```

## Compensation (`WhenEnter(Compensating)`)

A single `Compensating` entry-point handles every failure path. Each release is gated by a
saga flag so the same body covers validation-phase failures (no reservation, no persisted order)
and post-persist failures (full cleanup).

| Acquired | Released by | Gated on |
|---|---|---|
| Inventory reservation      | Publish `InventoryReleaseRequestedV1`                    | `ReservationId.HasValue` |
| Coupon lock                | `ICouponLockService.ReleaseAsync` (Redis Lua)            | `CouponLocked` |
| Flash sale stock (handler) | `IFlashSaleStockService.RestoreAsync` (Redis `INCRBY`)   | always (best-effort, try/catch) |
| Order row                  | `Order.Cancel(reason)` + publish `OrderCancelledV1`      | `OrderPersisted` |
| Pending slot               | `IPendingOrderSlotService.ReleaseAsync`                  | always (TTL safety net) |

Each Redis-backed compensation step (`RestoreAsync`, `ReleaseAsync`, `ConfirmUseAsync`) catches
transient failures and logs warnings — the underlying TTLs on `flashsale:{id}:stock` and
`coupon:{code}:locked-users` are the safety net so a Redis blip can never abort the rest of the
compensation chain. The cancellation publish carries `saga.FailureReason ?? ValidationError`
so downstream consumers (notifications, ledger) see human-readable text, not internal codes.

## Server-side pricing

```
originalPrice  = sum(catalog.CurrentPrice × quantity)   ← server
saleDiscount   = ISaleEligibilityService.SaleDiscountAmount
couponDiscount = ICouponLockService.DiscountAmount
shippingFee    = client-supplied (validated by ShippingValidator)
─────────────────────────────────────────────────────
finalTotal     = max(0, originalPrice - saleDiscount - couponDiscount + shippingFee)
```

If `|expectedTotal - finalTotal| / finalTotal > PriceMismatchTolerance` (default 1%), the saga
fails fast with `OrderErrors.PriceMismatch` (HTTP 409) and runs the validation compensation path.

## New abstractions (Application/Services)

| Interface | Implementation | Purpose |
|---|---|---|
| `ISaleEligibilityService`  | `PromotionSaleEligibilityClient` (Infrastructure) | `POST /api/v1/promotion/campaigns/{id}/validate` — returns sale discount + window |
| `ICouponLockService`       | `RedisCouponLockService` (Infrastructure)         | Redis Lua: atomic lock / release / confirm-use |

Both follow the Application-defines-port + Infrastructure-implements pattern (architecture rules
§1). Resilience for the HTTP client mirrors `ICatalogServiceClient` from TASK-05.

### Coupon lock Lua keys

```
coupon:{code}:eligible-users   (set)   — pre-populated by Promotion service
coupon:{code}:remaining        (string int)
coupon:{code}:locked-users     (set, TTL = CouponLockTtlSeconds, default 960 = 16 min)
coupon:{code}:used-users       (set, permanent)
coupon:{code}:meta-discount    (string)
coupon:{code}:meta-discount-type (string: "FIXED" | "PERCENT")
```

Reserve return values:
* `-1` already used · `-2` not eligible · `-3` already locked · `-4` exhausted
* bulk string `"<amount>|<type>"` on success

The Lua return is intentionally type-disambiguated (integer for error, bulk string for success)
so the C# adapter can branch on `RedisResult.Resp2Type` without an extra round-trip.

## Saga state additions

`PlaceSalesOrderSagaState` now tracks:

* Pricing: `OriginalPrice`, `Subtotal`, `SaleDiscount`, `CouponDiscount`, `ShippingFee`, `FinalTotal`,
  `ExpectedTotal`
* Sale window: `SaleStartAt`, `SaleEndAt`
* Catalog snapshot: `VariantsJson` (for `OrderFactory.BuildSalesFromSaga`)
* Side-effects: `OrderPersisted`, `ReservationId`, `CouponLocked` (replaces event-based
  `CouponClaimId`). `OrderPersisted` gates the OrderCancelled publish in compensation so
  validation-phase failures don't broadcast events for an order that never existed.
* Payment: `PaymentSessionId`, `PaymentUrl`, `QrCodeUrl`, `PaymentExpiresAt`
* Customer: `CustomerEmail`, `CustomerName`, `CustomerPhone`, `CustomerNote`, `ShippingAddressJson`
* Diagnostics: `ValidationError`, `FailureStep`, `FailureReason`
* Timeouts: `StepTimeoutTokenId`, `PaymentExpiryTokenId`

`UserId` is enforced to be a valid Guid at `SnapshotRequest` (throws if not) so downstream steps
parse without silent fallback. Migration generation is handled in TASK-12.

## Configuration

`appsettings.json`:

```json
{
  "PlaceOrder": {
    "MaxSalesPendingPerUser": 3,
    "CouponLockTtlSeconds": 960,
    "PriceMismatchTolerance": 0.01
  },
  "Order": {
    "Payment": {
      "SalesOrderExpiryMinutes": 15,
      "StepTimeoutSeconds": 30
    },
    "PromotionClient": {
      "BaseAddress": "http://localhost:5035",
      "Resilience": { /* Polly settings — same shape as CatalogClient */ }
    }
  }
}
```

All option classes carry `[Range]` annotations and `ValidateOnStart()` — invalid configuration
fails fast at host startup rather than at first request.

DI wired in `UrbanX.Order.Infrastructure.DependencyInjection.Extensions.ServiceCollectionExtensions`
via a generic `AddResilientHttpClient<TClient, TImpl, TClientOptions, TResilience>` helper that
both Catalog and Promotion clients share — every resilience option class implements
`IHttpClientResilienceOptions` so there's a single `ApplyResilience` body.

```csharp
services.AddResilientHttpClient<ICatalogServiceClient, CatalogServiceClient,
        CatalogClientOptions, CatalogClientResilienceOptions>(o => o.BaseAddress);
services.AddResilientHttpClient<ISaleEligibilityService, PromotionSaleEligibilityClient,
        PromotionClientOptions, PromotionClientResilienceOptions>(o => o.BaseAddress);
services.AddSingleton<ICouponLockService, RedisCouponLockService>();
```

## Promotion-team coordination (out of scope)

Promotion service needs to seed Redis keys on campaign / coupon creation:

1. **Campaign create:** `SET flashsale:{saleId}:stock = totalQuota`
2. **Coupon create:**
   - `SET coupon:{code}:remaining = totalQuota`
   - `SADD coupon:{code}:eligible-users <userIds…>`  *(or use rule-based eligibility)*
   - `SET coupon:{code}:meta-discount = <amount>`
   - `SET coupon:{code}:meta-discount-type = "FIXED" | "PERCENT"`
3. **Expose** `POST /api/v1/promotion/campaigns/{id}/validate` returning
   `{ startAt, endAt, saleDiscountAmount, discountType }`, or `400 { code: "SALE_EXPIRED" | "USER_ALREADY_BOUGHT" | "QUOTA_EXHAUSTED" }`.
4. **Reconcile** `coupon:{code}:used-users` → DB on cron.

Tracked under a separate ticket; Sales orders will fail closed (`OrderErrors.SalePricingUnavailable`)
until Promotion ships.

## Error codes (`OrderErrors`)

| Code | Trigger | HTTP |
|---|---|---|
| `Order.FlashSaleSoldOut` | Handler Redis DECRBY < 0 | 409 |
| `Order.SaleExpired` | Promotion 400 SALE_EXPIRED | 409 |
| `Order.UserAlreadyBoughtFromSale` | Promotion 400 USER_ALREADY_BOUGHT | 409 |
| `Order.PriceMismatch` | Server total differs > tolerance | 409 |
| `Order.CouponNotEligible` | Lua return `-2` | 403 |
| `Order.CouponAlreadyUsed` | Lua return `-1` (in `used-users`)  | 409 |
| `Order.CouponConcurrentClaim` | Lua return `-3` (concurrent lock holder; retry after TTL) | 409 |
| `Order.CouponExhausted` | Lua return `-4` | 410 |
| `Order.SalePricingUnavailable` | Promotion HTTP transport failure or unknown error code | 503 |

## Files touched

```
Application/Services/
  ISaleEligibilityService.cs                 (new)
  ICouponLockService.cs                      (new)
Application/Sagas/PlaceOrderSales/
  PlaceSalesOrderSagaState.cs                (rewrite)
  PlaceSalesOrderSagaStateMachine.cs         (rewrite)
  SagaTimeouts.cs                            (new)
Application/Usecases/V1/Command/Common/
  OrderFactory.cs                            (add BuildSalesFromSaga, SalesPricingSnapshot)

Infrastructure/DependencyInjection/
  Options/PromotionClientOptions.cs          (new)
  Extensions/ServiceCollectionExtensions.cs  (register Promotion HTTP + ICouponLockService)
Infrastructure/Services/
  PromotionSaleEligibilityClient.cs          (new)
  RedisCouponLockService.cs                  (new)

Persistence/Configurations/
  PlaceSalesOrderSagaStateConfiguration.cs   (rewrite for new columns)
Persistence/Repositories/
  SalesOrderStatusQuery.cs                   (CouponClaimId → null)

API/appsettings.json                         (add PromotionClient section)
```
