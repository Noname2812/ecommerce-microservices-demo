# TASK-01 — Extend Shared Integration Events

**Team:** Shared/Platform · **Effort:** S (0.5d) · **Depends:** —
**Branch:** `feature/order-refactor/TASK-01-shared-contracts`

## Mục đích

Mở rộng integration events trong `Shared.Contract` để chứa đủ data cho saga tạo Order (không phụ thuộc Order DB) + thêm 2 events mới: `ConfirmInventoryRequestedV1` và `OrderConfirmedV1`.

## Files

### Modify

**`src/Shared/Shared.Contract/Messaging/PlaceOrder/PlaceOrderRequestedV1.cs`** — thêm fields:
```csharp
public record PlaceOrderRequestedV1 : IntegrationEventBase
{
    public Guid OrderId { get; init; }
    public string UserId { get; init; } = "";
    public string IdempotencyKey { get; init; } = "";
    public string? CouponCode { get; init; }
    public decimal Subtotal { get; init; }
    public decimal ShippingFee { get; init; }
    public IReadOnlyList<NormalOrderItemSnapshot> Items { get; init; } = [];

    // NEW
    public ShippingAddressSnapshot ShippingAddress { get; init; } = default!;
    public string PricingSnapshot { get; init; } = "{}";
    public string CustomerEmail { get; init; } = "";
    public string CustomerName { get; init; } = "";
    public string? CustomerPhone { get; init; }
    public string? CustomerNote { get; init; }
}

public record ShippingAddressSnapshot(
    string FullName, string Phone, string Address,
    string Ward, string District, string City,
    string Province, string Country, string? ZipCode);
```

**`src/Shared/Shared.Contract/Messaging/PlaceOrderSaga/PlaceSalesOrderRequestedV1.cs`** — thêm cùng fields như Normal (CampaignId đã có).

### NEW

**`src/Shared/Shared.Contract/Messaging/PlaceOrderSaga/InventoryEvents.cs`** — append vào file hiện có:
```csharp
public record ConfirmInventoryRequestedV1 : IntegrationEventBase
{
    public Guid OrderId { get; init; }
    public Guid ReservationId { get; init; }
    public string IdempotencyKey { get; init; } = "";
}
```

**`src/Shared/Shared.Contract/Messaging/Order/OrderIntegrationEvents.cs`** — append:
```csharp
public record OrderConfirmedV1 : IntegrationEventBase
{
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = "";
    public Guid UserId { get; init; }
    public string CustomerEmail { get; init; } = "";
    public decimal FinalAmount { get; init; }
    public DateTimeOffset ConfirmedAt { get; init; }
    public IReadOnlyList<OrderItemSummary> Items { get; init; } = [];
}

public record OrderItemSummary(
    Guid ProductId,
    Guid VariantId,
    string Sku,
    int Quantity,
    decimal UnitPrice);
```

## Acceptance Criteria

- [ ] Build solution OK: `rtk err dotnet build UrbanX.sln`
- [ ] Tất cả existing handlers/consumers vẫn compile (backward-compatible — chỉ thêm fields, không đổi)
- [ ] Khôi phục `IntegrationEventBase` inheritance đúng
- [ ] Naming convention: `XxxV1` cho schema version 1

## Notes

- Field names dùng PascalCase, không có `[JsonPropertyName]` (tin tưởng MassTransit serializer default).
- `ShippingAddressSnapshot` là **record** (immutable) để dễ dedupe message.
- Không thêm field `OrderType` — Sales có CampaignId, Normal không có (đủ phân biệt).

## DoD

- [ ] Branch merge sau khi 2 reviewer approve
- [ ] Pre-merge: rebase từ main, resolve conflict
- [ ] Notify Order team + Inventory team unblock TASK-06, 07, 08, 09
