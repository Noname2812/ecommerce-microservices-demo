using System.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UrbanX.Order.Application.ReadModels;

namespace UrbanX.Order.Persistence.Repositories.Read;

internal sealed class DapperCatalogSnapshotWriter(OrderDbContext dbContext) : ICatalogSnapshotWriter
{
    public async Task UpsertVariantsAsync(IReadOnlyCollection<CatalogSnapshotRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return;

        const string sql = """
            INSERT INTO read.catalog_snapshots
                (variant_id, product_id, sku, product_is_active, variant_is_active,
                 current_price, projection_version, updated_at)
            VALUES (@VariantId, @ProductId, @Sku, @ProductIsActive, @VariantIsActive,
                    @CurrentPrice, @ProjectionVersion, @UpdatedAt)
            ON CONFLICT (variant_id) DO UPDATE
            SET product_id = EXCLUDED.product_id,
                sku = EXCLUDED.sku,
                product_is_active = EXCLUDED.product_is_active,
                variant_is_active = EXCLUDED.variant_is_active,
                current_price = EXCLUDED.current_price,
                projection_version = EXCLUDED.projection_version,
                updated_at = EXCLUDED.updated_at
            WHERE read.catalog_snapshots.projection_version < EXCLUDED.projection_version
            """;

        await ExecuteAsync(sql, rows, cancellationToken);
    }

    public async Task DeleteVariantsAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken)
    {
        if (variantIds.Count == 0) return;

        const string sql = "DELETE FROM read.catalog_snapshots WHERE variant_id = ANY(@VariantIds)";
        await ExecuteAsync(sql, new { VariantIds = variantIds.ToArray() }, cancellationToken);
    }

    public async Task UpdateProductStatusAsync(
        Guid productId,
        bool productIsActive,
        IReadOnlyCollection<Guid> affectedVariantIds,
        long projectionVersion,
        DateTime updatedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE read.catalog_snapshots
            SET product_is_active = @ProductIsActive,
                projection_version = @ProjectionVersion,
                updated_at = @UpdatedAt
            WHERE product_id = @ProductId
              AND projection_version < @ProjectionVersion
              AND (@VariantFilterEmpty OR variant_id = ANY(@VariantIds))
            """;

        var variantIds = affectedVariantIds.ToArray();
        await ExecuteAsync(sql, new
        {
            ProductId = productId,
            ProductIsActive = productIsActive,
            ProjectionVersion = projectionVersion,
            UpdatedAt = updatedAt,
            VariantIds = variantIds,
            VariantFilterEmpty = variantIds.Length == 0
        }, cancellationToken);
    }

    private async Task ExecuteAsync(string sql, object param, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        var transaction = dbContext.Database.CurrentTransaction is IDbContextTransaction tx
            ? tx.GetDbTransaction()
            : null;

        var command = new CommandDefinition(sql, param, transaction, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
