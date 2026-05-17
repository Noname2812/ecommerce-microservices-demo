using Dapper;
using Microsoft.EntityFrameworkCore;
using UrbanX.Order.Application.ReadModels;

namespace UrbanX.Order.Persistence.Repositories.Read;

internal sealed class DapperCatalogSnapshotReader(OrderDbContext dbContext) : ICatalogSnapshotReader
{
    private const string SelectColumns =
        "variant_id, product_id, sku, product_is_active, variant_is_active, current_price, projection_version, updated_at";

    public async Task<IReadOnlyDictionary<Guid, CatalogSnapshotRow>> GetByVariantIdsAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken)
    {
        if (variantIds.Count == 0)
            return new Dictionary<Guid, CatalogSnapshotRow>();

        var connection = dbContext.Database.GetDbConnection();

        var sql = $"SELECT {SelectColumns} FROM read.catalog_snapshots WHERE variant_id = ANY(@VariantIds)";
        var command = new CommandDefinition(sql,
            new { VariantIds = variantIds.ToArray() },
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<CatalogSnapshotRow>(command);
        return rows.ToDictionary(x => x.VariantId);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<CatalogSnapshotRow>>> GetByProductIdsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<CatalogSnapshotRow>>();

        var connection = dbContext.Database.GetDbConnection();

        var sql = $"SELECT {SelectColumns} FROM read.catalog_snapshots WHERE product_id = ANY(@ProductIds)";
        var command = new CommandDefinition(sql,
            new { ProductIds = productIds.ToArray() },
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<CatalogSnapshotRow>(command);
        return rows
            .GroupBy(x => x.ProductId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CatalogSnapshotRow>)g.ToList());
    }
}
