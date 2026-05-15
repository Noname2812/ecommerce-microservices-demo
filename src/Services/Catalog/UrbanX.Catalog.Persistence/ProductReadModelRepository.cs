using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shared.Kernel.Primitives;
using UrbanX.Catalog.Domain;
using UrbanX.Catalog.Domain.Errors;
using UrbanX.Catalog.Domain.ReadModels;
using UrbanX.Catalog.Domain.ValueObjects;

namespace UrbanX.Catalog.Persistence;

public sealed class ProductReadModelRepository(
    [FromKeyedServices("catalog-read")] NpgsqlDataSource dataSource) : IProductReadRepository
{
    public const string ActivitySourceName = "UrbanX.Catalog";
    public const string MeterName = "UrbanX.Catalog";

    private static readonly ActivitySource _source = new(ActivitySourceName, "1.0.0");
    private static readonly Meter _meter = new(MeterName, "1.0.0");
    private static readonly Histogram<double> _searchDurationMs =
        _meter.CreateHistogram<double>("catalog.db.search_duration_ms", "ms",
            "Total SearchAsync duration: connection open + query + mapping");
    private static readonly Histogram<double> _connectionOpenMs =
        _meter.CreateHistogram<double>("catalog.db.connection_open_ms", "ms",
            "Time waiting for a connection from the read pool");

    // Captures COUNT(*) OVER() total alongside each row from the single-round-trip search query.
    // Dapper ignores the _rank column (no matching property); MatchNamesWithUnderscores maps total_count → TotalCount.
    private sealed class SearchResultRow
    {
        public long TotalCount { get; set; }
        public Guid ProductId { get; set; }
        public Guid SellerId { get; set; }
        public string Sku { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string Slug { get; set; } = null!;
        public string Status { get; set; } = null!;
        public Guid? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public Guid? BrandId { get; set; }
        public string? BrandName { get; set; }
        public string? ShortDescription { get; set; }
        public decimal BasePrice { get; set; }
        public string? PrimaryImageUrl { get; set; }
        public string[] Tags { get; set; } = [];
        public DateTimeOffset UpdatedAt { get; set; }
        public DateTimeOffset? DeletedAt { get; set; }
        public int ProjectionVersion { get; set; }
        public string NameNormalized { get; set; } = string.Empty;
        public string SkuNormalized { get; set; } = string.Empty;

        public ProductListView ToView() => new()
        {
            ProductId = ProductId, SellerId = SellerId, Sku = Sku,
            Name = Name, Slug = Slug, Status = Status,
            CategoryId = CategoryId, CategoryName = CategoryName,
            BrandId = BrandId, BrandName = BrandName,
            ShortDescription = ShortDescription, BasePrice = BasePrice,
            PrimaryImageUrl = PrimaryImageUrl, Tags = Tags,
            UpdatedAt = UpdatedAt, DeletedAt = DeletedAt,
            ProjectionVersion = ProjectionVersion,
            NameNormalized = NameNormalized, SkuNormalized = SkuNormalized,
        };
    }

    public async Task<ProductDetailView?> GetByIdAsync(Guid productId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        var cmd = new CommandDefinition(
            "SELECT * FROM read.product_detail_view WHERE product_id = @productId AND deleted_at IS NULL",
            new { productId },
            cancellationToken: ct);

        return await conn.QuerySingleOrDefaultAsync<ProductDetailView>(cmd);
    }

    public async Task<Result<CursorPageResult<ProductListView>>> GetPageKeysetAsync(
        Guid? sellerId, Guid? categoryId, string? status,
        string? cursor, int pageSize, CancellationToken ct = default)
    {
        if (!TryDecodeCursor(cursor, out var cursorUpdatedAt, out var cursorProductId))
            return Result.Failure<CursorPageResult<ProductListView>>(CatalogErrors.InvalidCursor(cursor!));

        await using var conn = await dataSource.OpenConnectionAsync(ct);

        var safeSize = Math.Clamp(pageSize, 1, PageResult<ProductListView>.UpperPageSize);
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

        return Result.Success(CursorPageResult<ProductListView>.Create(items, nextCursor));
    }

    public async Task<PageResult<ProductListView>> SearchAsync(
        string q, Guid? categoryId, decimal? priceMin, decimal? priceMax,
        string sort, int page, int pageSize, CancellationToken ct = default)
    {
        using var searchSpan = _source.StartActivity("catalog.product.search", ActivityKind.Internal);
        searchSpan?.SetTag("db.system", "postgresql");
        searchSpan?.SetTag("catalog.search.sort", sort);
        searchSpan?.SetTag("catalog.search.page", page);
        searchSpan?.SetTag("catalog.search.page_size", pageSize);
        searchSpan?.SetTag("catalog.search.has_category_filter", categoryId.HasValue);
        searchSpan?.SetTag("catalog.search.has_price_filter", priceMin.HasValue || priceMax.HasValue);

        var totalSw = Stopwatch.StartNew();

        // Measure connection pool wait separately — pool exhaustion shows up here under load.
        var connSw = Stopwatch.StartNew();
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        connSw.Stop();
        _connectionOpenMs.Record(connSw.Elapsed.TotalMilliseconds);
        searchSpan?.AddEvent(new ActivityEvent("db.connection_acquired",
            tags: new() { { "pool", "catalog-read" }, { "duration_ms", connSw.ElapsedMilliseconds } }));

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

        // _rank expression and ORDER BY clause per sort mode.
        // For non-relevance sorts both tiers use the same column so _rank is a dummy 0.
        var (ftsRank, trgRank, sortClause) = sort switch
        {
            "price_asc"  => ("0::float8", "0::float8", "ORDER BY base_price ASC"),
            "price_desc" => ("0::float8", "0::float8", "ORDER BY base_price DESC"),
            "newest"     => ("0::float8", "0::float8", "ORDER BY updated_at DESC"),
            _ => (
                "ts_rank(search_vector, to_tsquery('simple', unaccent(@tsquery)))::float8",
                "similarity(name_normalized, @rawQ)::float8",
                "ORDER BY _rank DESC"
            ),
        };

        // Single round-trip CTE:
        //   fts_data    — FTS candidates (MATERIALIZED so it is computed once).
        //   has_fts     — boolean flag: true when FTS returns any row.
        //   trg_data    — trigram candidates, only executed when FTS is empty (NOT has_fts.v).
        //   combined    — union of whichever tier produced results; exactly one tier is non-empty.
        //   COUNT(*) OVER() — total rows in combined before LIMIT, returned alongside each page row.
        var sql = $"""
            WITH fts_data AS MATERIALIZED (
                SELECT *
                FROM read.product_list_view
                WHERE search_vector @@ to_tsquery('simple', unaccent(@tsquery))
                  AND {baseFilter}
            ),
            has_fts AS (SELECT COUNT(*) > 0 AS v FROM fts_data),
            trg_data AS (
                SELECT *
                FROM read.product_list_view
                WHERE NOT (SELECT v FROM has_fts)
                  AND name_normalized % @rawQ
                  AND {baseFilter}
            ),
            combined AS (
                SELECT *, {ftsRank} AS _rank FROM fts_data
                UNION ALL
                SELECT *, {trgRank} AS _rank FROM trg_data
            )
            SELECT COUNT(*) OVER() AS total_count, combined.*
            FROM combined
            {sortClause}
            LIMIT @limit OFFSET @offset;
            """;

        var p = new DynamicParameters();
        p.Add("tsquery", tsquery);
        p.Add("rawQ", NormalizeForTrigram(q));
        p.Add("categoryId", categoryId);
        p.Add("priceMin", priceMin);
        p.Add("priceMax", priceMax);
        p.Add("activeStatus", ProductStatus.Active);
        p.Add("limit", safeSize);
        p.Add("offset", offset);

        List<SearchResultRow> rows;
        using (var querySpan = _source.StartActivity("catalog.db.search_products", ActivityKind.Client))
        {
            querySpan?.SetTag("db.system", "postgresql");
            querySpan?.SetTag("db.operation", "SELECT");
            querySpan?.SetTag("db.collection.name", "read.product_list_view");
            querySpan?.SetTag("catalog.search.sort", sort);
            querySpan?.SetTag("catalog.search.page", page);
            querySpan?.SetTag("catalog.search.offset", offset);

            rows = (await conn.QueryAsync<SearchResultRow>(
                new CommandDefinition(sql, p, cancellationToken: ct))).ToList();

            querySpan?.SetTag("db.response.rows_returned", rows.Count);
        }

        var total = rows.Count > 0 ? (int)rows[0].TotalCount : 0;
        var items = rows.ConvertAll(r => r.ToView());

        totalSw.Stop();
        searchSpan?.SetTag("catalog.result.total_count", total);
        _searchDurationMs.Record(totalSw.Elapsed.TotalMilliseconds,
            new TagList { { "sort", sort } });

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

    private static bool TryDecodeCursor(
        string? cursor,
        out DateTimeOffset? updatedAt,
        out Guid? productId)
    {
        updatedAt = null;
        productId = null;
        if (string.IsNullOrEmpty(cursor)) return true;
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var sep = raw.LastIndexOf('|');
            if (sep < 0) return false;
            updatedAt = DateTimeOffset.Parse(raw[..sep], null, System.Globalization.DateTimeStyles.RoundtripKind);
            productId = Guid.Parse(raw[(sep + 1)..]);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
