namespace UrbanX.Order.Application.Abstractions.Catalog;

public sealed record ProductSnapshot(
    Guid ProductId,
    bool Exists,
    bool IsActive,
    DateTime CachedAt);

public sealed record VariantPriceSnapshot(
    Guid VariantId,
    decimal CurrentPrice,
    DateTime CachedAt);
