# TASK-02 — Order Domain Refactor

**Team:** Order · **Effort:** M (1.5d) · **Depends:** —
**Branch:** `feature/order-refactor/TASK-02-domain`

## Mục đích

Sửa `Order` aggregate để hỗ trợ 4-state flow đúng semantics (`PROCESSING → PENDING_PAYMENT → CONFIRMED / CANCELLED`), bổ sung idempotent guards và đổi `Order.Create` để nhận `orderId` từ ngoài.

## Files

### Modify

**`src/Services/Order/UrbanX.Order.Domain/Models/OrderStatus.cs`**

```csharp
public static class OrderStatus
{
    public const string Processing      = "PROCESSING";
    public const string PendingPayment  = "PENDING_PAYMENT";
    public const string Confirmed       = "CONFIRMED";
    public const string Cancelled       = "CANCELLED";

    // Future logistics (giữ — không touch trong scope này)
    public const string Shipped         = "SHIPPED";
    public const string Delivered       = "DELIVERED";
    public const string RefundRequested = "REFUND_REQUESTED";
    public const string Refunded        = "REFUNDED";
}
```

⚠ DROP `Pending` constant cũ. Search-replace `OrderStatus.Pending` → `OrderStatus.Processing` hoặc `OrderStatus.PendingPayment` tùy context.

**`src/Services/Order/UrbanX.Order.Domain/Models/Order.cs`** — refactor:

1. **Thêm fields snapshot cho Sales pricing:**
```csharp
public decimal OriginalPrice  { get; private set; }   // NEW — pre-discount sum
public decimal SaleDiscount   { get; private set; }   // NEW — flash sale discount (separate từ couponDiscount)
// Existing: CouponDiscount, DiscountAmount, ShippingFee, TaxAmount, TotalAmount, FinalAmount
```

Verify computed semantics:
- `OriginalPrice = sum(items.UnitPrice × Quantity)` — pre-discount
- `DiscountAmount = SaleDiscount + CouponDiscount + item-level discounts`
- `FinalAmount = OriginalPrice - DiscountAmount + ShippingFee + TaxAmount`

2. **`Order.Create` — đổi signature nhận `Guid orderId` + pricing fields:**
```csharp
public static Order Create(
    Guid orderId,                    // NEW — saga truyền ticketId
    string orderNumber,
    Guid userId,
    string customerEmail,
    string customerName,
    string? customerPhone,
    ShippingAddress shippingAddress,
    decimal shippingFee,
    string? couponCode,
    decimal couponDiscount,
    decimal saleDiscount,            // NEW — flash sale discount (Sales only, default 0 for Normal)
    decimal originalPrice,           // NEW — pre-discount sum (server-calc)
    string? customerNote,
    string idempotencyKey,
    IReadOnlyList<NewOrderItemSpec> items,
    string orderType = Models.OrderType.Normal,
    Guid? campaignId = null)
{
    // ... existing logic ...
    Id = orderId,                     // KHÔNG còn Guid.NewGuid()
    Status = OrderStatus.Processing,  // initial = PROCESSING
    OriginalPrice = originalPrice,
    SaleDiscount  = saleDiscount,
    // history entry: prev=null, next=PROCESSING, note="Order created"
}
```

⚠ Normal handler/saga gọi `Order.Create(..., saleDiscount: 0, originalPrice: subtotal, ...)`. Sales gọi với giá trị thực từ saga.

2. **DROP methods:** `SetConfirmedWithReservation`, `SetConfirmedAsSalesOrder`, `SetPaymentSession`

3. **NEW `MarkReadyForPayment`:**
```csharp
public void MarkReadyForPayment(
    Guid reservationId, Guid? claimId,
    string paymentUrl, string? qrCodeUrl,
    Guid changedById, string changedByName)
{
    if (Status != OrderStatus.Processing) return;   // idempotent guard

    var prev = Status;
    ReservationId = reservationId;
    CouponClaimId = claimId;
    PaymentUrl = paymentUrl;
    QrCodeUrl = qrCodeUrl;
    PaymentStatus = Models.PaymentStatus.AwaitingPayment;
    Status = OrderStatus.PendingPayment;
    UpdatedAt = DateTimeOffset.UtcNow;
    _statusHistory.Add(OrderStatusHistory.Create(
        Id, prev, OrderStatus.PendingPayment, "Awaiting payment", changedById, changedByName));
}
```

4. **DROP `MarkSalesReadyForPayment`** — KHÔNG cần method Sales riêng. `OrderType=Sales`, `CampaignId`, `SaleDiscount`, `OriginalPrice` đã được set tại `Order.Create` (saga truyền các giá trị này). Sales saga gọi `order.MarkReadyForPayment(...)` chung với Normal.

5. **Refactor `MarkPaid` — 3 guards:**
```csharp
public void MarkPaid(string paymentSessionId, Guid changedById, string changedByName)
{
    if (Status == OrderStatus.Cancelled) return;
    if (Status == OrderStatus.Confirmed && PaymentStatus == Models.PaymentStatus.Paid) return;
    if (Status != OrderStatus.PendingPayment)
        throw new DomainException($"Cannot mark paid in status {Status}");

    var prev = Status;
    Status = OrderStatus.Confirmed;
    PaymentStatus = Models.PaymentStatus.Paid;
    PaymentReference = paymentSessionId;
    UpdatedAt = DateTimeOffset.UtcNow;
    _statusHistory.Add(OrderStatusHistory.Create(
        Id, prev, OrderStatus.Confirmed, "Payment completed", changedById, changedByName));
}
```

6. **Refactor `Cancel` — idempotent:**
```csharp
public void Cancel(string reason, Guid? changedById, string? changedByName)
{
    if (Status == OrderStatus.Cancelled) return;   // idempotent guard
    // ... existing logic ...
}
```

7. **Refactor `CanBeCancelledBy`** — update để bao gồm `Processing`, `PendingPayment`:
```csharp
public bool CanBeCancelledBy(Guid userId) =>
    (Status == OrderStatus.Processing
     || Status == OrderStatus.PendingPayment
     || Status == OrderStatus.Confirmed)
    && UserId == userId;
```

### Modify Errors

**`src/Services/Order/UrbanX.Order.Domain/Errors/OrderErrors.cs`** — thêm:
```csharp
// Common
public static readonly Error TooManyPendingOrders =
    new("Order.TooManyPending", "User has reached maximum pending orders");
public static readonly Error TicketNotFound =
    new("Order.TicketNotFound", "Ticket not found");

// Catalog
public static Error CatalogValidationFailed(string reason) =>
    new("Order.CatalogValidationFailed", reason);
public static readonly Error CatalogUnavailable =
    new("Order.CatalogUnavailable", "Catalog service unavailable");

// Sales — thêm để align với TASK-08 flow (Flash sale + Coupon + Server-side pricing)
public static Error FlashSaleSoldOut(Guid saleId) =>
    new("Order.FlashSaleSoldOut", $"Flash sale {saleId} is sold out");
public static readonly Error SaleExpired =
    new("Order.SaleExpired", "Flash sale has expired");
public static readonly Error UserAlreadyBoughtFromSale =
    new("Order.UserAlreadyBoughtFromSale", "User already bought from this sale");
public static readonly Error PriceMismatch =
    new("Order.PriceMismatch", "Server-calculated price differs from expected (>1%)");
public static readonly Error CouponNotEligible =
    new("Order.CouponNotEligible", "User is not eligible for this coupon");
public static readonly Error CouponAlreadyUsed =
    new("Order.CouponAlreadyUsed", "User has already used this coupon");
public static readonly Error CouponExhausted =
    new("Order.CouponExhausted", "Coupon has no remaining quota");
```

**API mapping (`ApiEndpoint.ToOrderResult`)** — verify thêm:
- `Order.TooManyPending` → 429
- `Order.FlashSaleSoldOut`, `Order.SaleExpired`, `Order.PriceMismatch`, `Order.CouponAlreadyUsed`, `Order.UserAlreadyBoughtFromSale` → 409 Conflict
- `Order.CouponNotEligible` → 403 Forbidden
- `Order.CouponExhausted` → 410 Gone
- `Order.CatalogUnavailable` → 503

## Acceptance Criteria

- [ ] Build OK, không touch caller (caller sẽ fix trong TASK-06, 07, 08)
- [ ] Unit tests cover:
  - `Order.Create` với `orderId` từ ngoài → Status=PROCESSING
  - `MarkReadyForPayment` trên PROCESSING → PENDING_PAYMENT
  - `MarkReadyForPayment` trên non-PROCESSING → no-op
  - `MarkPaid` trên PENDING_PAYMENT → CONFIRMED
  - `MarkPaid` trên Cancelled → no-op (idempotent)
  - `MarkPaid` trên Confirmed+Paid → no-op (idempotent)
  - `MarkPaid` trên Processing → throw DomainException
  - `Cancel` 2 lần → 1 entry history (idempotent)
- [ ] Coverage > 90% cho file Order.cs

## Notes

- Skill `unit-test-writer` để generate tests.
- `OrderStatusHistory.Create(Id, prev=PROCESSING, next=PROCESSING)` cũng có thể dùng cho history "Inventory reserved" — verify constraint nếu có unique key `(order_id, prev, next, timestamp)`.

## DoD

- [ ] All unit tests pass
- [ ] PR review approve + merge
- [ ] Unblock TASK-06, 07, 08
