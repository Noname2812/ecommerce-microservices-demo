using System.Text;
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

    public async Task<CursorPageResult<ProductListView>> GetPageKeysetAsync(
        Guid? sellerId, Guid? categoryId, string? status,
        string? cursor, int pageSize, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        var safeSize = Math.Clamp(pageSize, 1, PageResult<ProductListView>.UpperPageSize);
        var (cursorUpdatedAt, cursorProductId) = DecodeCursor(cursor);

        var where = new List<string> { "deleted_at IS NULL", "status != @deletedStatus" };
        var p = new DynamicParameters();
        p.Add("deletedStatus", ProductStatus.Deleted);
        p.Add("limit", safeSize + 1);

        if (sellerId.HasValue)   { where.Add("seller_id = @sellerId");     p.Add("sellerId", sellerId); }
        if (categoryId.HasValue) { where.Add("category_id = @categoryId"); p.Add("categoryId", categoryId); }
        if (!string.IsNullOrEmpty(status)) { where.Add("status = @status"); p.Add("status", status); }

        if (cursorUpdatedAt.HasValue)
        {
            // Row-value comparison: skip rows at or before cursor position
            where.Add("(updated_at < @cursorUpdatedAt OR (updated_at = @cursorUpdatedAt AND product_id < @cursorProductId))");
            p.Add("cursorUpdatedAt", cursorUpdatedAt.Value);
            p.Add("cursorProductId", cursorProductId!.Value);
        }

        var whereClause = string.Join(" AND ", where);

        var sql = $"""
            SELECT * FROM read.product_list_view
            WHERE {whereClause}
            ORDER BY updated_at DESC, product_id DESC
            LIMIT @limit
            """;

        var rows = (await conn.QueryAsync<ProductListView>(new CommandDefinition(sql, p, cancellationToken: ct))).ToList();

        var hasMore = rows.Count > safeSize;
        var items = hasMore ? rows.Take(safeSize).ToList() : rows;

        string? nextCursor = hasMore && items.Count > 0
            ? EncodeCursor(items[^1].UpdatedAt, items[^1].ProductId)
            : null;

        return CursorPageResult<ProductListView>.Create(items, nextCursor);
    }

    public async Task<PageResult<ProductListView>> SearchAsync(
        string q, Guid? categoryId, decimal? priceMin, decimal? priceMax,
        string sort, int page, int pageSize, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        var safeSize = Math.Clamp(pageSize, 1, PageResult<ProductListView>.UpperPageSize);
        var offset = (page - 1) * safeSize;

        // Build prefix-match tsquery: "điện thoại" → "điện:* & thoại:*"
        // f_unaccent() in SQL strips Vietnamese diacritics before passing to to_tsquery
        var tsquery = BuildTsQuery(q);
        if (string.IsNullOrEmpty(tsquery))
            return PageResult<ProductListView>.Create([], page, safeSize, 0);

        var baseFilter = """
            deleted_at IS NULL
            AND status = @activeStatus
            AND (@categoryId::uuid IS NULL OR category_id = @categoryId)
            AND (@priceMin::numeric IS NULL OR base_price >= @priceMin)
            AND (@priceMax::numeric IS NULL OR base_price <= @priceMax)
            """;

        var orderByRelevance = "ORDER BY ts_rank(search_vector, to_tsquery('simple', public.f_unaccent(@tsquery))) DESC";
        var orderClause = sort switch
        {
            "price_asc"  => "ORDER BY base_price ASC",
            "price_desc" => "ORDER BY base_price DESC",
            "newest"     => "ORDER BY updated_at DESC",
            _            => orderByRelevance
        };

        var trgOrderClause = sort switch
        {
            "price_asc"  => "ORDER BY base_price ASC",
            "price_desc" => "ORDER BY base_price DESC",
            "newest"     => "ORDER BY updated_at DESC",
            _            => "ORDER BY similarity(name_normalized, @rawQ) DESC"
        };

        var p = new DynamicParameters();
        p.Add("tsquery", tsquery);
        // rawQ: lowercased + unaccented in C# for trigram fallback (matches name_normalized column)
        p.Add("rawQ", NormalizeForTrigram(q));
        p.Add("categoryId", categoryId);
        p.Add("priceMin", priceMin);
        p.Add("priceMax", priceMax);
        p.Add("activeStatus", ProductStatus.Active);
        p.Add("limit", safeSize);
        p.Add("offset", offset);

        // Tier 1: full-text search (exact + prefix match, Vietnamese via f_unaccent on DB side)
        var ftSql = $"""
            SELECT COUNT(*) FROM read.product_list_view
            WHERE search_vector @@ to_tsquery('simple', public.f_unaccent(@tsquery))
            AND {baseFilter};

            SELECT * FROM read.product_list_view
            WHERE search_vector @@ to_tsquery('simple', public.f_unaccent(@tsquery))
            AND {baseFilter}
            {orderClause}
            LIMIT @limit OFFSET @offset;
            """;

        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(ftSql, p, cancellationToken: ct));
        var total = await multi.ReadSingleAsync<int>();
        var items = (await multi.ReadAsync<ProductListView>()).ToList();

        if (total > 0)
            return PageResult<ProductListView>.Create(items, page, safeSize, total);

        // Tier 2: trigram similarity fallback (handles typos, abbreviations)
        var trgSql = $"""
            SELECT COUNT(*) FROM read.product_list_view
            WHERE name_normalized % @rawQ
            AND {baseFilter};

            SELECT * FROM read.product_list_view
            WHERE name_normalized % @rawQ
            AND {baseFilter}
            {trgOrderClause}
            LIMIT @limit OFFSET @offset;
            """;

        using var multi2 = await conn.QueryMultipleAsync(new CommandDefinition(trgSql, p, cancellationToken: ct));
        total = await multi2.ReadSingleAsync<int>();
        items = (await multi2.ReadAsync<ProductListView>()).ToList();

        return PageResult<ProductListView>.Create(items, page, safeSize, total);
    }

    // Builds prefix-match tsquery string; SQL applies f_unaccent() before evaluating
    // Example: "điện thoại" → "điện:* & thoại:*"
    private static string BuildTsQuery(string q)
    {
        var terms = q
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => new string(t.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray()))
            .Where(t => t.Length > 0)
            .ToArray();

        return terms.Length == 0 ? string.Empty : string.Join(" & ", terms.Select(t => t + ":*"));
    }

    // Normalize query for trigram comparison: lowercase + Unicode NFD diacritic stripping
    // Matches name_normalized = lower(f_unaccent(name)) stored by the DB trigger
    private static string NormalizeForTrigram(string q)
    {
        var normalized = q.Trim()
            .Normalize(System.Text.NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC).ToLowerInvariant();
    }

    private static string EncodeCursor(DateTimeOffset updatedAt, Guid productId)
    {
        var raw = $"{updatedAt.UtcDateTime:O}|{productId}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    private static (DateTimeOffset?, Guid?) DecodeCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return (null, null);
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var sep = raw.LastIndexOf('|');
            if (sep < 0) return (null, null);
            var updatedAt = DateTimeOffset.Parse(raw[..sep], null, System.Globalization.DateTimeStyles.RoundtripKind);
            var productId = Guid.Parse(raw[(sep + 1)..]);
            return (updatedAt, productId);
        }
        catch
        {
            return (null, null);
        }
    }
}
