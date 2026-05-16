# TASK-01 · Saga Contract Events (Shared.Contract)

| | |
|---|---|
| **Effort** | ~0.5 ngày |
| **Depends on** | — (start ngay) |
| **Blocks** | TASK-02, TASK-03, TASK-04 |
| **Branch** | `feat/saga/task-01-contract-events` |

## Goal

Định nghĩa toàn bộ message contracts dùng giữa Order saga và 3 service (Promotion, Inventory, Coupon). Đây là foundation cho mọi task sau — các consumer + saga state machine sẽ reference những type này.

## Context

Hiện tại Order service gọi sync HTTP đến Promotion / Inventory / Coupon. Sau migration sẽ chuyển sang **request/response event qua RabbitMQ**, mỗi service expose 1 consumer xử lý event request và publish lại event response cho saga catch.

CorrelationId convention = OrderId (string GUID format "D") để MassTransit saga match instance.

## Files (all new)

Tạo folder `src/Shared/Shared.Contract/Messaging/PlaceOrderSaga/`:

```
PlaceOrderSaga/
├── PlaceSalesOrderRequestedV1.cs    # Trigger: Order → Saga
├── PromotionEvents.cs                # Request + 2 Response (Promotion)
├── InventoryEvents.cs                # Request + 2 Response (Inventory)
├── CouponEvents.cs                   # Request + 2 Response (Coupon)
└── PlaceSalesOrderFailedV1.cs        # Terminal failure
```

## Event specifications

### 1. `PlaceSalesOrderRequestedV1`

Trigger event Order API publish qua outbox khi user POST `/api/v1/orders/sales`. Saga consume event này để khởi tạo state machine.

**Source**: `"order-service"`

**Fields**:
- `OrderId` (Guid) — saga `CorrelationId`
- `UserId` (string)
- `CampaignId` (Guid)
- `IdempotencyKey` (string)
- `Subtotal` (decimal)
- `ShippingFee` (decimal)
- `ShippingAddress` (object snapshot)
- `CouponCode` (string?) — null nếu không có
- `Items` (list of `{ ProductId, VariantId, Quantity, UnitPrice }`)
- `PricingSnapshot` (object — đã validate ở sync handler)
- `CustomerEmail` (string?)
- `CustomerNote` (string?)

### 2. `PromotionEvents.cs` (3 events)

#### `RedeemSalePromotionRequestedV1`
- **Source**: `"order-service"`
- Saga → Promotion service consumer
- Fields: `OrderId`, `UserId`, `CampaignId`, `CouponCode?`, `Subtotal`, `Items[]`

#### `PromotionRedeemedV1`
- **Source**: `"promotion-service"`
- Promotion → Saga (success)
- Fields: `OrderId`, `OrderLevelDiscount`, `ItemDiscounts[]`, `AppliedPromotionIds[]`, `ClaimedFlashSaleSlots[]` (chứa `PromotionId`, `SlotKey`, `Quantity`)

#### `PromotionRedeemFailedV1`
- **Source**: `"promotion-service"`
- Promotion → Saga (failure)
- Fields: `OrderId`, `ErrorCode` (string), `ErrorMessage` (string)

### 3. `InventoryEvents.cs` (3 events)

#### `ReserveInventoryRequestedV1`
- **Source**: `"order-service"`
- Saga → Inventory
- Fields: `OrderId`, `OrderIdempotencyKey` (`{IdempotencyKey}:inv`), `Items[]` (`ProductId, VariantId, Quantity`)

#### `InventoryReservedV1`
- **Source**: `"inventory-service"`
- Inventory → Saga
- Fields: `OrderId`, `ReservationId` (Guid), `ExpiresAt` (DateTimeOffset), `Items[]`

#### `InventoryReserveFailedV1`
- **Source**: `"inventory-service"`
- Inventory → Saga
- Fields: `OrderId`, `ErrorCode`, `ErrorMessage`, `OutOfStockProducts[]` (list of `ProductId`, `Available`)

### 4. `CouponEvents.cs` (3 events)

#### `ClaimCouponRequestedV1`
- **Source**: `"order-service"`
- Saga → Promotion (coupon endpoint)
- Fields: `OrderId`, `OrderIdempotencyKey` (`{IdempotencyKey}:cpn`), `UserId`, `CouponCode`, `OrderTotal`

#### `CouponClaimedV1`
- **Source**: `"promotion-service"`
- Fields: `OrderId`, `ClaimId` (Guid), `DiscountAmount` (decimal), `ExpiresAt` (DateTimeOffset)

#### `CouponClaimFailedV1`
- **Source**: `"promotion-service"`
- Fields: `OrderId`, `ErrorCode`, `ErrorMessage`

### 5. `PlaceSalesOrderFailedV1`

- **Source**: `"order-service"` (publish bởi saga)
- Saga → external listeners (notification, analytics, etc.)
- Fields: `OrderId`, `UserId`, `FailureStep` (string — vd `"PromotionRedeem"`), `FailureReason`, `OccurredAt`

## Implementation rules

1. **Tất cả kế thừa `IntegrationEventBase`** ([src/Shared/Shared.Contract/Abstractions/IntegrationEventBase.cs](../../../src/Shared/Shared.Contract/Abstractions/IntegrationEventBase.cs)).
2. **Override `Source`** với tên service publish event.
3. **Records (immutable)** — dùng C# `public record TypeNameV1 : IntegrationEventBase { ... }`.
4. **Không** reference type ngoài `Shared.Kernel` + `Shared.Contract.Abstractions`. Nested type (vd `ClaimedFlashSaleSlot`) tạo bên trong cùng file, là record riêng.
5. **DateTimeOffset** cho timestamp (UTC).
6. Naming convention: tất cả suffix `V1` cho versioning.

## Example skeleton

```csharp
namespace Shared.Contract.Messaging.PlaceOrderSaga;

public record PlaceSalesOrderRequestedV1 : IntegrationEventBase
{
    public override string Source => "order-service";

    public required Guid OrderId { get; init; }
    public required string UserId { get; init; }
    public required Guid CampaignId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required decimal Subtotal { get; init; }
    public required decimal ShippingFee { get; init; }
    public required ShippingAddressDto ShippingAddress { get; init; }
    public string? CouponCode { get; init; }
    public required IReadOnlyList<OrderItemDto> Items { get; init; }
    public string? CustomerEmail { get; init; }
    public string? CustomerNote { get; init; }
}

public record OrderItemDto(Guid ProductId, Guid VariantId, int Quantity, decimal UnitPrice);
public record ShippingAddressDto(/* fields */);
```

## Acceptance criteria

- [ ] 5 files created đúng folder `src/Shared/Shared.Contract/Messaging/PlaceOrderSaga/`.
- [ ] Build solution thành công: `dotnet build UrbanX.sln`.
- [ ] Mỗi event override `Source` đúng service name.
- [ ] Không file nào reference `Shared.Application` / `Shared.Messaging` / service-specific assembly.
- [ ] PR description include diagram (text) tóm tắt flow events.

## Testing

Task này chưa cần unit test riêng — events là pure data record. Test sẽ được cover ở TASK-02 (saga state machine) và TASK-03/04 (consumers).

## Reference

- Existing events: [src/Shared/Shared.Contract/Messaging/Order/OrderIntegrationEvents.cs](../../../src/Shared/Shared.Contract/Messaging/Order/OrderIntegrationEvents.cs)
- Existing pattern: [src/Shared/Shared.Contract/Messaging/Catalog/ProductCreated.cs](../../../src/Shared/Shared.Contract/Messaging/Catalog/ProductCreated.cs)
- `IntegrationEventBase`: [src/Shared/Shared.Contract/Abstractions/IntegrationEventBase.cs](../../../src/Shared/Shared.Contract/Abstractions/IntegrationEventBase.cs)
