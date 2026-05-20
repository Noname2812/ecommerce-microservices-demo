namespace UrbanX.Inventory.Domain;

/// <summary>
/// Atomic-CAS access pattern: handler issues per-item UPDATE … WHERE invariant-still-holds.
/// PostgreSQL row lock during UPDATE serializes concurrent writes for ≤ a few ms (no SELECT + UPDATE
/// gap means no xmin conflict). Multi-item callers must sort by <see cref="Models.InventoryItem.Id"/>
/// to keep lock-acquisition order consistent across transactions and avoid deadlock.
/// </summary>
public interface IInventoryItemRepository
{
    /// <summary>
    /// Returns <c>product_id → primary inventory_item_id</c> (lowest id when multiple variants/warehouses
    /// exist for one product). Mapping only — no row lock acquired here; the atomic UPDATE methods
    /// take the lock.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Guid>> GetPrimaryItemIdsByProductAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomic CAS reserve: <c>UPDATE … SET quantity_reserved = quantity_reserved + qty WHERE quantity_on_hand - quantity_reserved &gt;= qty</c>.
    /// Returns 1 on success, 0 when stock insufficient at the moment of UPDATE.
    /// </summary>
    Task<int> TryReserveAtomicAsync(
        Guid itemId,
        int quantity,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads current available (<c>quantity_on_hand − quantity_reserved</c>). Used only to enrich the
    /// OutOfStock error after <see cref="TryReserveAtomicAsync"/> returns 0.
    /// </summary>
    Task<int> GetAvailableQuantityAsync(Guid itemId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomic decrement of reserved counter (release path).
    /// Returns 1 on success, 0 on invariant violation (<c>quantity_reserved &lt; qty</c>) — caller treats as fatal.
    /// </summary>
    Task<int> DecrementReservedAtomicAsync(
        Guid itemId,
        int quantity,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomic hard-deduct (confirm path): decrements both <c>quantity_reserved</c> and <c>quantity_on_hand</c>.
    /// Returns the pre-update <c>quantity_on_hand</c> (for the stock_movement audit row), or <c>null</c>
    /// when the invariant guard rejects the update.
    /// </summary>
    Task<int?> ConfirmDeductAtomicAsync(
        Guid itemId,
        int quantity,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);
}
