namespace UrbanX.Order.Application.ReadModels;

public sealed record CatalogSnapshotRow(
    Guid VariantId,
    Guid ProductId,
    string Sku,
    bool ProductIsActive,
    bool VariantIsActive,
    decimal CurrentPrice,
    long ProjectionVersion,
    DateTime UpdatedAt);
