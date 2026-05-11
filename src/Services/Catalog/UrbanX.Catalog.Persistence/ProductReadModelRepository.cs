using Dapper;
using Npgsql;
using Shared.Kernel.Primitives;
using UrbanX.Catalog.Domain;
using UrbanX.Catalog.Domain.ReadModels;
using UrbanX.Catalog.Domain.ValueObjects;

namespace UrbanX.Catalog.Persistence;

public sealed class ProductReadModelRepository(NpgsqlDataSource dataSource) : IProductReadRepository
{
    public async Task<ProductDetailView?> GetByIdAsync(Guid productId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        var cmd = new CommandDefinition(
            "SELECT * FROM read.product_detail_view WHERE product_id = @productId AND deleted_at IS NULL",
            new { productId },
            cancellationToken: ct);

        return await conn.QuerySingleOrDefaultAsync<ProductDetailView>(cmd);
    }

    public async Task<PageResult<ProductListView>> GetPageAsync(
        Guid? sellerId, Guid? categoryId, string? status,
        int page, int pageSize, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        var safeSize = Math.Clamp(pageSize, 1, PageResult<ProductListView>.UpperPageSize);
        var offset = (page - 1) * safeSize;

        var where = new List<string> { "deleted_at IS NULL", "status != @deletedStatus" };
        var p = new DynamicParameters();
        p.Add("deletedStatus", ProductStatus.Deleted);

        if (sellerId.HasValue)   { where.Add("seller_id = @sellerId");     p.Add("sellerId", sellerId); }
        if (categoryId.HasValue) { where.Add("category_id = @categoryId"); p.Add("categoryId", categoryId); }
        if (!string.IsNullOrEmpty(status)) { where.Add("status = @status"); p.Add("status", status); }

        var whereClause = string.Join(" AND ", where);
        p.Add("limit", safeSize);
        p.Add("offset", offset);

        var sql = $"""
            SELECT COUNT(*) FROM read.product_list_view WHERE {whereClause};
            SELECT * FROM read.product_list_view WHERE {whereClause} ORDER BY updated_at DESC LIMIT @limit OFFSET @offset;
            """;

        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(sql, p, cancellationToken: ct));

        var total = await multi.ReadSingleAsync<int>();
        var items = (await multi.ReadAsync<ProductListView>()).ToList();

        return PageResult<ProductListView>.Create(items, page, safeSize, total);
    }
}
