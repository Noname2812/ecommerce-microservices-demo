# Product Search

Full-text + trigram fuzzy search cho sản phẩm, hỗ trợ tiếng Việt via `unaccent`.

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
- `search_vector` column (tsvector) được build tự động bởi PostgreSQL trigger `trg_product_list_view_search`
- Vector = `name` (weight A) + `short_description` (weight B), dùng `simple` dictionary + `f_unaccent()`
- Query: `to_tsquery('simple', f_unaccent('<term1>:* & <term2>:*'))`
- Prefix matching: `iphone` → match "iPhone 15 Pro"

**Tier 2 — Trigram GIN (fallback, khi tier 1 = 0 kết quả):**
- `name_normalized` column = `lower(f_unaccent(name))`
- Trigram similarity operator `%` (threshold default 0.3)
- Bắt typo: "iphne" → match "iphone"

## Vietnamese Normalization

- PostgreSQL: `f_unaccent()` — immutable wrapper của `unaccent` extension, xử lý dấu tiếng Việt
  - `điện thoại` → `dien thoai`
  - Lưu ý: `đ` (U+0111) được convert sang `d` bởi `unaccent` dictionary
- C# (trigram query): `NormalizeForTrigram()` — Unicode NFD decomposition, strip NonSpacingMark

## DB Objects (migration `20260511100000_AddProductSearch`)

| Object | Type | Purpose |
|---|---|---|
| `unaccent` | Extension | Strip diacritics |
| `pg_trgm` | Extension | Trigram similarity |
| `public.f_unaccent(text)` | Function IMMUTABLE | Wrapper để dùng unaccent trong GIN expression index |
| `read.product_list_view.search_vector` | tsvector column | Full-text index target |
| `read.product_list_view.name_normalized` | text column | Trigram index target |
| `read.trg_fn_product_list_view_search` | Trigger function | Tự compute search_vector + name_normalized |
| `trg_product_list_view_search` | BEFORE INSERT/UPDATE trigger | Fire khi name/short_description thay đổi |
| `idx_plv_search_vector` | GIN index | Full-text search |
| `idx_plv_name_normalized_trgm` | GIN trigram index | Fuzzy search fallback |
| `idx_plv_category_id_base_price` | B-tree index | Filter category + sort by price |
| `idx_plv_keyset` | B-tree partial index | Keyset pagination cho list mode |

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
