# Promotion Saga Consumers

Promotion service hiện chỉ còn **một** luồng tích hợp với Order qua saga:

| Luồng | Cơ chế | Command |
|---|---|---|
| PlaceOrder (Normal/Sales) — coupon | Async RabbitMQ consumer `ClaimCouponRequestedConsumer` | `ClaimCouponCommand` |
| Sync HTTP `POST /api/v1/promotions/redeem` (Order pre-place validation) | HTTP endpoint | `RedeemPromotionCommand` |

> **Lưu ý:** `RedeemSalePromotionRequestedConsumer` đã bị **xoá** khỏi codebase. Saga `PlaceOrderNormal` / `PlaceOrderSales` hiện không publish `RedeemSalePromotionRequestedV1` ở bất kỳ state nào → consumer cũ là dead code. Nếu sau này cần restore Sales-only quota flow, lấy lại từ git history.

---

## ClaimCouponRequestedConsumer

**File:** [src/Services/Promotion/UrbanX.Promotion.Application/Messaging/ClaimCouponRequested/ClaimCouponRequestedConsumer.cs](../../src/Services/Promotion/UrbanX.Promotion.Application/Messaging/ClaimCouponRequested/ClaimCouponRequestedConsumer.cs)
**ConsumerDefinition:** [src/Services/Promotion/UrbanX.Promotion.API/Messaging/ClaimCouponRequested/ClaimCouponRequestedConsumerDefinition.cs](../../src/Services/Promotion/UrbanX.Promotion.API/Messaging/ClaimCouponRequested/ClaimCouponRequestedConsumerDefinition.cs)

**Event consumed:** `ClaimCouponRequestedV1` (`Shared.Contract.Messaging.PlaceOrderSaga`)
**Publisher:** Order saga — khi vào state `CouponClaiming` (chỉ khi order có coupon code)

### Mapping

```
ClaimCouponRequestedV1 → ClaimCouponCommand
  OrderIdempotencyKey → IdempotencyKey  (idempotency built-in)
  CouponCode          → CouponCode
  UserId (string)     → UserId (Guid.Parse)
  OrderTotal          → OrderAmount
```

### Response events

| Event | Khi nào | Payload chính |
|---|---|---|
| `CouponClaimedV1` | Claim thành công | `ClaimId`, `DiscountAmount`, `ExpiresAt` |
| `CouponClaimFailedV1` | Claim thất bại | `ErrorCode`, `ErrorMessage` |

**Idempotency:** `ClaimCouponCommand` dùng `IdempotencyKey = OrderIdempotencyKey` — replay cùng event trả về claim hiện có thay vì tạo mới.

---

## CouponReleaseRequestedConsumer (compensation)

**File:** [src/Services/Promotion/UrbanX.Promotion.Application/Messaging/CouponReleaseRequested/CouponReleaseRequestedConsumer.cs](../../src/Services/Promotion/UrbanX.Promotion.Application/Messaging/CouponReleaseRequested/CouponReleaseRequestedConsumer.cs)
**ConsumerDefinition:** [src/Services/Promotion/UrbanX.Promotion.API/Messaging/CouponReleaseRequested/CouponReleaseRequestedConsumerDefinition.cs](../../src/Services/Promotion/UrbanX.Promotion.API/Messaging/CouponReleaseRequested/CouponReleaseRequestedConsumerDefinition.cs) — bind vào fanout exchange `compensation.events`.

**Event consumed:** `CouponReleaseRequestedV1` (`Shared.Contract.Messaging.PlaceOrder`)
**Publisher:** Order saga — compensation khi saga fail sau khi đã claim coupon.

Command: `ReleaseCouponClaimCommand` qua `CouponReleaseRequestedProcessor`.

---

## Endpoint configuration

Tuning được đọc từ `appsettings.json`, không hardcode trong code.

| Setting | Section | Mặc định |
|---|---|---|
| Queue name | `Promotion:Messaging:ClaimCouponRequested:QueueName` | `promotion-claim-coupon-requested` |
| Queue name | `Promotion:Messaging:CouponReleaseRequested:QueueName` | `promotion-coupon-release-requested` |
| Retry (Claim) | `…:Retry:{RetryLimit,MinIntervalMs,MaxIntervalMs,IntervalDeltaMs}` | 3x exponential, 200ms–2s |
| Retry (Release) | `…:Retry:{Intervals,IntervalSeconds}` | 3 × 5s |
| Throughput | `…:{PrefetchCount,ConcurrentMessageLimit}` | 16 / 8 |

Options classes: `Application/DependencyInjection/Options/`
Validators chạy tại startup (`ValidateOnStart`) — misconfiguration fail-fast.

---

## Error handling

- **Business failure** (`result.IsFailure`): consumer publish failure event, không throw → message acked, saga nhận failure event và chuyển sang `Compensating`.
- **Transient exception** (timeout, network, `CouponReleaseCommandFailedException`): `IntegrationEventConsumerBase` rethrow → MassTransit retry policy (exponential / interval) xử lý.
- **Retry exhausted**: message vào dead-letter queue → saga timeout (Coupon: 5s) → tự chuyển `Compensating` độc lập.
