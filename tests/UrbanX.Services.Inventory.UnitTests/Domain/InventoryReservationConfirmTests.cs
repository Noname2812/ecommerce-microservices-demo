using UrbanX.Inventory.Domain.Models;
using UrbanX.Inventory.Domain.ValueObjects;

namespace UrbanX.Services.Inventory.UnitTests.Domain;

public sealed class InventoryReservationConfirmTests
{
    private static readonly DateTimeOffset Utc = DateTimeOffset.Parse("2026-05-18T12:00:00Z");

    [Fact]
    public void Confirm_FromPending_SetsConfirmedStatus()
    {
        var reservation = InventoryReservation.CreatePending(
            Guid.NewGuid(),
            Guid.NewGuid(),
            2,
            Utc.AddHours(1),
            Utc);

        reservation.Confirm(Utc);

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Equal(Utc, reservation.ConfirmedAt);
    }

    [Fact]
    public void Confirm_WhenAlreadyConfirmed_IsIdempotent()
    {
        var reservation = InventoryReservation.CreatePending(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            Utc.AddHours(1),
            Utc);
        reservation.Confirm(Utc);

        reservation.Confirm(Utc.AddMinutes(5));

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
    }

    [Fact]
    public void MarkReleased_SetsReleasedStatus()
    {
        var reservation = InventoryReservation.CreatePending(
            Guid.NewGuid(),
            Guid.NewGuid(),
            3,
            Utc.AddHours(1),
            Utc);

        reservation.MarkReleased(Utc);

        Assert.Equal(ReservationStatus.Released, reservation.Status);
        Assert.Equal(Utc, reservation.ReleasedAt);
    }
}
