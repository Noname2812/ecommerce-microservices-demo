# TASK-09 — Inventory: ConfirmReservation (Hard Deduct)

**Team:** Inventory · **Effort:** M (1.5d) · **Depends:** TASK-01
**Branch:** `feature/order-refactor/TASK-09-inventory-confirm`

## Mục đích

Thêm command + consumer cho Inventory service để chuyển reservation từ "soft lock" sang "hard deduct" khi Order CONFIRMED. Đúng semantic Reserve → Deduct.

## Files

### Domain

**`src/Services/Inventory/UrbanX.Inventory.Domain/Models/InventoryReservation.cs`** — thêm:
```csharp
public DateTimeOffset? ConfirmedAt { get; private set; }

public void Confirm(DateTimeOffset utcNow)
{
    if (Status == InventoryReservationStatus.Confirmed) return;  // idempotent
    if (Status != InventoryReservationStatus.Pending)
        throw new DomainException(
            $"Cannot confirm reservation in status {Status}");

    Status = InventoryReservationStatus.Confirmed;
    ConfirmedAt = utcNow;
    UpdatedAt = utcNow;
}
```

**`src/Services/Inventory/UrbanX.Inventory.Domain/Models/InventoryReservationStatus.cs`** (verify enum/constants) — thêm `Confirmed`:
```csharp
public static class InventoryReservationStatus
{
    public const string Pending   = "PENDING";
    public const string Confirmed = "CONFIRMED";  // NEW — sau hard deduct
    public const string Released  = "RELEASED";
}
```

**`src/Services/Inventory/UrbanX.Inventory.Domain/Models/InventoryItem.cs`** — thêm:
```csharp
public void ConfirmDeduction(int quantity, DateTimeOffset utcNow)
{
    if (quantity <= 0)
        throw new DomainException("Confirm quantity must be positive");
    if (quantity > QuantityReserved)
        throw new DomainException(
            $"Cannot confirm {quantity}; only {QuantityReserved} reserved");

    QuantityReserved -= quantity;
    QuantityOnHand   -= quantity;       // hard deduct!
    UpdatedAt = utcNow;
}
```

### Application — Command

**NEW folder:** `src/Services/Inventory/UrbanX.Inventory.Application/Usecases/V1/Command/ConfirmReservation/`

**`ConfirmReservationCommand.cs`:**
```csharp
[RequirePermission(Permissions.Inventory.Write)]
public record ConfirmReservationCommand(
    Guid ReservationId,
    string IdempotencyKey)
    : ICommand, IConcurrencyRetriableCommand;

public sealed class ConfirmReservationCommandValidator
    : AbstractValidator<ConfirmReservationCommand>
{
    public ConfirmReservationCommandValidator()
    {
        RuleFor(x => x.ReservationId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(100);
    }
}
```

**`ConfirmReservationCommandHandler.cs`:**
```csharp
internal sealed class ConfirmReservationCommandHandler(
    IInventoryReservationRepository reservationRepo,
    IInventoryItemRepository itemRepo,
    IStockMovementRepository stockMovementRepo,
    IUserContext userContext,
    TimeProvider timeProvider,
    ILogger<ConfirmReservationCommandHandler> logger)
    : ICommandHandler<ConfirmReservationCommand>
{
    public async Task<Result> Handle(ConfirmReservationCommand cmd, CancellationToken ct)
    {
        var reservation = await reservationRepo.GetByIdAsync(cmd.ReservationId, ct);
        if (reservation is null)
            return Result.Failure(InventoryErrors.ReservationNotFound(cmd.ReservationId));

        // Idempotent: nếu đã confirmed → return success
        if (reservation.Status == InventoryReservationStatus.Confirmed)
        {
            logger.LogInformation("Reservation {Id} already confirmed, skip", cmd.ReservationId);
            return Result.Success();
        }

        var item = await itemRepo.GetByVariantAndWarehouseAsync(
            reservation.VariantId, reservation.WarehouseId, ct);
        if (item is null)
            return Result.Failure(InventoryErrors.ItemNotFound(reservation.VariantId));

        var utcNow = timeProvider.GetUtcNow();
        reservation.Confirm(utcNow);
        item.ConfirmDeduction(reservation.Quantity, utcNow);

        var movement = StockMovement.CreateOutbound(
            itemId:         item.Id,
            variantId:      item.VariantId,
            warehouseId:    item.WarehouseId,
            quantityChange: -reservation.Quantity,
            referenceType:  StockMovementReferenceType.Order,
            referenceId:    reservation.OrderId,
            reason:         "ORDER_CONFIRMED",
            createdById:    userContext.UserId,
            createdByName:  userContext.FullName ?? "",
            utcNow:         utcNow);
        await stockMovementRepo.AddAsync(movement, ct);

        return Result.Success();
    }
}
```

### Application — Consumer

**NEW:** `src/Services/Inventory/UrbanX.Inventory.Application/Messaging/PlaceOrderSaga/ConfirmInventoryRequestedConsumer.cs`

```csharp
using MassTransit;
using MediatR;
using Shared.Contract.Messaging.PlaceOrderSaga;
using Shared.Messaging;
using UrbanX.Inventory.Application.Usecases.V1.Command.ConfirmReservation;

namespace UrbanX.Inventory.Application.Messaging.PlaceOrderSaga;

public sealed class ConfirmInventoryRequestedConsumer(ISender sender)
    : IntegrationEventConsumerBase<ConfirmInventoryRequestedV1, ConfirmInventoryRequestedConsumer>(sender)
{
    public override async Task Consume(ConsumeContext<ConfirmInventoryRequestedV1> context)
    {
        var msg = context.Message;
        var cmd = new ConfirmReservationCommand(msg.ReservationId, msg.IdempotencyKey);
        var result = await Sender.Send(cmd, context.CancellationToken);

        if (result.IsFailure)
        {
            // Log error; MT retry sẽ kick in nếu IConcurrencyRetriableCommand
            // Domain errors (NotFound, etc.) → log + don't retry (poison message → _error queue)
            throw new InvalidOperationException(
                $"ConfirmReservation failed: {result.Error.Code} — {result.Error.Description}");
        }
    }
}
```

### Inventory.API/Program.cs

Thêm trong `bus =>`:
```csharp
bus.AddConsumer<ConfirmInventoryRequestedConsumer>();
```

### Errors

**`Inventory.Domain/Errors/InventoryErrors.cs`** — verify hoặc thêm:
- `ReservationNotFound(Guid id)`
- `ItemNotFound(Guid variantId)`

### Migration

**`UrbanX.Inventory.Persistence/Configurations/InventoryReservationConfiguration.cs`** — thêm column config:
```csharp
builder.Property(x => x.ConfirmedAt).IsRequired(false);
```

Run migration:
```
cd src/Services/Inventory/UrbanX.Inventory.Persistence
dotnet ef migrations add AddReservationConfirmedAt
```

Verify migration sinh:
```csharp
migrationBuilder.AddColumn<DateTimeOffset>(
    name: "confirmed_at",
    table: "inventory_reservations",
    type: "timestamp with time zone",
    nullable: true);
```

## Acceptance Criteria

- [ ] Build OK
- [ ] Unit tests:
  - `InventoryReservation.Confirm` trên Pending → Status=Confirmed, ConfirmedAt set
  - `Confirm` trên Confirmed → no-op (idempotent)
  - `Confirm` trên Released → throw DomainException
  - `InventoryItem.ConfirmDeduction(qty)` → QuantityReserved -= qty, QuantityOnHand -= qty
  - `ConfirmDeduction(qty > QuantityReserved)` → throw
  - Handler: happy path → reservation + item + movement đều persist
  - Handler: reservation đã Confirmed → return Success (no DB write)
  - Handler: reservation not found → return Failure
- [ ] Integration test:
  - Send `ConfirmInventoryRequestedV1` qua MT → reservation Status=Confirmed, item QuantityOnHand giảm
  - Send 2 lần cùng ReservationId → chỉ deduct 1 lần
- [ ] Migration apply OK: `dotnet ef database update`

## Notes

- `IConcurrencyRetriableCommand` đã có ở Inventory — MT retry tự động khi PostgreSQL `xmin` conflict
- Consumer **không** sử dụng `IPublishEndpoint` để publish event ngược (TASK-07 saga đã handle `ConfirmInventoryRequestedV1` là fire-and-forget từ saga perspective)
- Tương lai có thể publish `InventoryConfirmedV1` để saga track (optional, nếu cần audit hard deduct hoàn tất)

## DoD

- [ ] Tests pass
- [ ] Migration applied
- [ ] PR merge
- [ ] Notify Order team unblock TASK-07, 08
