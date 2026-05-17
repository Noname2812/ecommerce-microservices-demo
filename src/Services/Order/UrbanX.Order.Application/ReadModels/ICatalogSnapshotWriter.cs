namespace UrbanX.Order.Application.ReadModels;

/// <summary>
/// Upsert API used by Catalog event consumers to maintain the local read model.
/// Implementations should be idempotent: skip rows whose stored ProjectionVersion already exceeds the incoming version.
/// Runs within the consumer's EF transaction (via UoW) so projection + ProcessedEvent commit atomically.
/// </summary>
public interface ICatalogSnapshotWriter
{
    Task UpsertVariantsAsync(IReadOnlyCollection<CatalogSnapshotRow> rows, CancellationToken cancellationToken);

    Task DeleteVariantsAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken);

    Task UpdateProductStatusAsync(
        Guid productId,
        bool productIsActive,
        IReadOnlyCollection<Guid> affectedVariantIds,
        long projectionVersion,
        DateTime updatedAt,
        CancellationToken cancellationToken);
}
