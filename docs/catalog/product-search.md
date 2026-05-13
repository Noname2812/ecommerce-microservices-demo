# Product Search

Full-text + trigram fuzzy search cho sản phẩm, hỗ trợ tiếng Việt via .NET-side normalization.

## Endpoint

```
GET /api/v1/catalog/products?q=điện+thoại&categoryId=<uuid>&priceMin=100&priceMax=999&sort=price_asc&page=1&pageSize=20
```

| Param | Type | Required | Description |
|---|---|---|---|
| `q` | string | Yes (search mode) | Từ khóa tìm kiếm (max 200 chars) |
| `categoryId` | guid | No | Filter theo danh mục |
| `priceMin` | decimal | No | Giá tối thiểu |
| `priceMax` | decimal | No | Giá tối đa |
| `sort` | string | No | `relevance` (default) \| `price_asc` \| `price_desc` \| `newest` |
| `page` | int | No | Default 1 |
| `pageSize` | int | No | Default 20, max 100 |

> **Giới hạn depth:** `page × pageSize ≤ 200` — tối đa 200 kết quả mỗi phiên search.
> Nếu cần thêm kết quả, hãy dùng filter `categoryId` / `priceMin` / `priceMax` để thu hẹp.

Nếu không có `q`, endpoint chuyển sang list mode với keyset pagination — xem [product-list.md](product-list.md).

## Search Strategy (2-tier)

**Tier 1 — Full-text GIN (fast path):**
- `search_vector` column (tsvector) — PostgreSQL `GENERATED ALWAYS AS STORED` column, tự recompute khi `name_normalized` / `sku_normalized` / `tags` thay đổi
- Vector = `name_normalized` + `sku_normalized` + `array_to_string(tags,' ')`, dùng `simple` dictionary
- Query: `search_vector @@ to_tsquery('simple', '<term1>:* & <term2>:*')`
- Prefix matching: `iphone` → match "iPhone 15 Pro"

**Tier 2 — Trigram GIN (fallback, khi tier 1 = 0 kết quả):**
- `name_normalized` column — bắt typo, partial match
- Trigram similarity operator `%` (threshold default 0.3)
- Bắt typo: `"iphne"` → match `"iphone"`

## Vietnamese Normalization

Normalization hoàn toàn ở **application layer** (`ProductProjectionBuilder.NormalizeText()`):

```csharp
// Persistence/Projections/ProductProjectionBuilder.cs
private static string NormalizeText(string? input)
{
    if (string.IsNullOrEmpty(input)) return string.Empty;
    var decomposed = input.Normalize(NormalizationForm.FormD);   // NFD: tách combining marks
    var sb = new StringBuilder(decomposed.Length);
    foreach (var c in decomposed)
    {
        if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            sb.Append(c);                                         // bỏ dấu
    }
    return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
}
```

- `"Điện Thoại"` → `"dien thoai"`
- `"đ"` (U+0111) không phải combining mark → **giữ nguyên** là `"d"` sau NFD decomposition
- Không cần PostgreSQL `unaccent` extension — normalization xảy ra khi projection rebuild

**Query phía Dapper cũng cần normalize** từ khóa tìm kiếm trước khi so sánh với `name_normalized`:

```sql
-- Trigram search (fuzzy name)
WHERE name_normalized % lower(:query)

-- Full-text search
WHERE search_vector @@ to_tsquery('simple', :normalizedQuery || ':*')

-- Tags chứa tag
WHERE tags @> ARRAY[:tag]
```

## DB Objects (migration `20260513125724_InitialCreate`)

| Object | Type | Purpose |
|---|---|---|
| `pg_trgm` | Extension | Trigram similarity (`%` operator, `gin_trgm_ops`) |
| `read.product_list_view.name_normalized` | `varchar(500)` | .NET-computed lowercase+unaccented name, trigram index target |
| `read.product_list_view.sku_normalized` | `varchar(100)` | .NET-computed lowercase SKU, trigram index target |
| `read.product_list_view.search_vector` | `tsvector` GENERATED STORED | PostgreSQL auto-computes từ `name_normalized + sku_normalized + tags` |
| `ix_plv_tags_gin` | GIN index | `tags @> ARRAY[...]` |
| `ix_plv_name_normalized_trgm` | GIN trigram index | Fuzzy / partial name search |
| `ix_plv_sku_normalized_trgm` | GIN trigram index | Partial SKU search |
| `ix_plv_search_vector_gin` | GIN index | Full-text search |

### Computed column SQL

```sql
search_vector GENERATED ALWAYS AS (
  to_tsvector('simple',
    coalesce(name_normalized,'') || ' ' ||
    coalesce(sku_normalized,'') || ' ' ||
    coalesce(array_to_string(tags,' '),''))
) STORED
```

## Projection Flow

```
ProductCreatedV1 / ProductInfoUpdatedV1
        ↓ (via Outbox → RabbitMQ → Consumer)
ProductProjectionBuilder.Build(product)
        ├── NameNormalized = NormalizeText(product.Name)
        ├── SkuNormalized  = NormalizeText(product.Sku)
        └── Tags = product.Tags.ToArray()
        ↓ (EF SaveChanges)
PostgreSQL INSERT/UPDATE product_list_views
        ↓ (computed column auto-recompute)
search_vector updated
```

## Response

```json
{
  "items": [
    {
      "id": "uuid",
      "name": "iPhone 15 Pro",
      "slug": "iphone-15-pro",
      "status": "ACTIVE",
      "categoryId": "uuid",
      "categoryName": "Điện thoại",
      "basePrice": 28990000,
      "primaryImageUrl": "https://...",
      "tags": ["apple", "smartphone"]
    }
  ],
  "pageIndex": 1,
  "pageSize": 20,
  "totalCount": 42,
  "hasNextPage": true,
  "hasPreviousPage": false
}
```

`hasNextPage = true` → FE hiển thị nút "Xem thêm", gọi lại với `page + 1`.
