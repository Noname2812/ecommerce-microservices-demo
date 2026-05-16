# Promotion Saga Consumers

Promotion service xử lý hai luồng tích hợp với Order:

| Luồng | Cơ chế | Command |
|---|---|---|
| PlaceOrder normal | Sync HTTP `POST /api/v1/promotions/redeem` | `RedeemPromotionCommand` |
| PlaceSalesOrder saga | Async RabbitMQ consumers (tài liệu này) | `RedeemPromotionCommand`, `ClaimCouponCommand` |

Cả hai luồng đều tái sử dụng cùng MediatR command — không có business logic nào bị duplicate.

---

## RedeemSalePromotionRequestedConsumer

**File:** `Application/Messaging/Consumers/RedeemSalePromotionRequestedConsumer.cs`

**Event consumed:** `RedeemSalePromotionRequestedV1` (`Shared.Contract.Messaging.PlaceOrderSaga`)  
**Publisher:** Order saga — khi vào state `PromotionRedeeming`

### Mapping

```
RedeemSalePromotionRequestedV1 → RedeemPromotionCommand
  UserId (string)       → CustomerId (Guid.Parse)
  OrderId               → OrderId
  Subtotal              → Subtotal
  CouponCode            → CouponCode
  Items[]               → Items[] (PromotionOrderItem → RedeemOrderItem)
```

### Response events

| Event | Khi nào | Payload chính |
|---|---|---|
| `PromotionRedeemedV1` | Redeem thành công | `OrderLevelDiscount`, `ItemDiscounts`, `AppliedPromotionIds`, `ClaimedFlashSaleSlots` |
| `PromotionRedeemFailedV1` | Redeem thất bại | `ErrorCode`, `ErrorMessage` |

`CorrelationId = OrderId.ToString("D")` — saga dùng để correlate response.  
`CausationId = event.EventId.ToString()` — traceability ngược về trigger event.

### ClaimedFlashSaleSlots

`RedeemPromotionResult` (kết quả của command) giờ có thêm field `ClaimedFlashSaleSlots` (`IReadOnlyList<ClaimedFlashSaleSlotResult>`). Handler theo dõi từng slot được claim trong vòng lặp flash sale và trả về `(PromotionId, SlotKey, Quantity)` cho từng item. Consumer map sang `ClaimedFlashSaleSlot` của saga contract để saga có thể compensation (release slot) khi cần.

---

## ClaimCouponRequestedConsumer

**File:** `Application/Messaging/Consumers/ClaimCouponRequestedConsumer.cs`

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

## Endpoint configuration

Tuning được đọc từ `appsettings.json`, không hardcode trong code.

| Setting | Section | Mặc định |
|---|---|---|
| Queue name | `Promotion:Messaging:RedeemSalePromotionRequested:QueueName` | `promotion-redeem-sale-promotion-requested` |
| Queue name | `Promotion:Messaging:ClaimCouponRequested:QueueName` | `promotion-claim-coupon-requested` |
| Retry | `…:Retry:{RetryLimit,MinIntervalMs,MaxIntervalMs,IntervalDeltaMs}` | 3x exponential, 200ms–2s |
| Throughput | `…:{PrefetchCount,ConcurrentMessageLimit}` | 16 / 8 |

Options classes: `Application/DependencyInjection/Options/`  
ConsumerDefinition classes: `API/Messaging/`  
Validators chạy tại startup (`ValidateOnStart`) — misconfiguration fail-fast.

---

## Error handling

- **Business failure** (`result.IsFailure`): consumer publish failure event, không throw → message acked, saga nhận failure event và chuyển sang `Compensating`.
- **Transient exception** (timeout, network): `IntegrationEventConsumerBase` rethrow → MassTransit retry policy (exponential) xử lý.
- **Retry exhausted**: message vào dead-letter queue → saga timeout 30s → tự chuyển `Compensating` độc lập.
