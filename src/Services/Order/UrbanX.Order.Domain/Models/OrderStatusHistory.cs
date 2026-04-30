using Shared.Kernel.Domain;

namespace UrbanX.Order.Domain.Models;

public sealed class OrderStatusHistory : BaseEntity<Guid>
{
    public Guid OrderId { get; private set; }
    public string? FromStatus { get; private set; }
    public string ToStatus { get; private set; } = null!;
    public string? Note { get; private set; }
    public Guid? ChangedById { get; private set; }
    public string? ChangedByName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private OrderStatusHistory() { }

    internal static OrderStatusHistory Create(
        Guid orderId,
        string? fromStatus,
        string toStatus,
        string? note,
        Guid? changedById,
        string? changedByName) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        FromStatus = fromStatus,
        ToStatus = toStatus,
        Note = note,
        ChangedById = changedById,
        ChangedByName = changedByName,
        CreatedAt = DateTimeOffset.UtcNow
    };
}
