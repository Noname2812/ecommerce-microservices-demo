using UrbanX.Inventory.Domain.Errors;
using UrbanX.Inventory.Domain.Models;
using UrbanX.Inventory.Domain.ValueObjects;

namespace UrbanX.Services.Inventory.UnitTests.Domain;

public class InventoryReservationConfirmTests
{
    private static readonly DateTimeOffset Utc = DateTimeOffset.Parse("2026-05-18T12:00:00Z");

    [Fact]
    public void Confirm_WhenPending_SetsConfirmedStatusAndConfirmedAt()
    {
        var reservation = InventoryReservation.CreatePending(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "idem",
            1,
            Utc.AddHours(1),
            Utc.AddMinutes(-1));

        reservation.Confirm(Utc);

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Equal(Utc, reservation.ConfirmedAt);
        Assert.Equal(Utc, reservation.UpdatedAt);
    }

    [Fact]
    public void Confirm_WhenAlreadyConfirmed_IsIdempotentNoOp()
    {
        var reservation = InventoryReservation.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "idem", 1, Utc.AddHours(1), Utc);
        reservation.Confirm(Utc.AddHours(-1));
        var confirmedAt = reservation.ConfirmedAt;
        var updatedAt = reservation.UpdatedAt;

        reservation.Confirm(Utc);

        Assert.Equal(confirmedAt, reservation.ConfirmedAt);
        Assert.Equal(updatedAt, reservation.UpdatedAt);
    }

    [Fact]
    public void Confirm_WhenReleased_ThrowsDomainException()
    {
        var reservation = InventoryReservation.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "idem", 1, Utc.AddHours(1), Utc);
        reservation.MarkReleased(Utc);

        var ex = Assert.Throws<InventoryDomainException>(() => reservation.Confirm(Utc));

        Assert.Contains("RELEASED", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
