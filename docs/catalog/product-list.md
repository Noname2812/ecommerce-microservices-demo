# Product List — Keyset Pagination

Danh sách sản phẩm dùng **keyset pagination** (cursor-based) để đảm bảo hiệu năng hằng số tại mọi độ sâu, phù hợp với UI infinite scroll / "Xem thêm".

## Endpoint

```
GET /api/v1/catalog/products?sellerId=<uuid>&categoryId=<uuid>&status=ACTIVE&cursor=<token>&pageSize=20
```

| Param | Type | Required | Description |
|---|---|---|---|
| `sellerId` | guid | No | Filter theo seller |
| `categoryId` | guid | No | Filter theo danh mục |
| `status` | string | No | `DRAFT` \| `ACTIVE` \| `INACTIVE` |
| `cursor` | string | No | Opaque token từ response trước. Bỏ qua (hoặc null) cho trang đầu |
| `pageSize` | int | No | Default 20, max 100 |

> `q` không được truyền — nếu có `q` thì endpoint chuyển sang search mode với offset pagination, xem [product-search.md](product-search.md).

## Cách hoạt động

Mỗi response trả về `nextCursor`. FE dùng token này làm `cursor` cho request tiếp theo.

```
Trang 1:  GET /products?pageSize=20
          ← { items: [...], nextCursor: "abc123", hasMore: true }

Trang 2:  GET /products?cursor=abc123&pageSize=20
          ← { items: [...], nextCursor: "xyz789", hasMore: true }

Trang N:  GET /products?cursor=xyz789&pageSize=20
          ← { items: [...], nextCursor: null, hasMore: false }
          → hasMore = false: ẩn nút "Xem thêm"
```

## Response

```json
{
  "items": [
    {
      "id": "uuid",
      "sku": "SKU-001",
      "name": "iPhone 15 Pro",
      "slug": "iphone-15-pro",
      "status": "ACTIVE",
      "categoryId": "uuid",
      "categoryName": "Điện thoại",
      "brandId": "uuid",
      "brandName": "Apple",
      "basePrice": 28990000,
      "primaryImageUrl": "https://...",
      "tags": ["apple", "smartphone"],
      "updatedAt": "2026-05-11T10:00:00Z"
    }
  ],
  "nextCursor": "dXBkYXRlZEF0fHByb2R1Y3RJZA==",
  "hasMore": true
}
```

## Cursor format

Cursor là base64 của chuỗi `{updatedAt ISO8601}|{productId}`, encode ở server, opaque với client. Không parse hay tự tạo cursor ở FE.

## DB Index

Migration `20260513125724_InitialCreate` tạo partial index:

```sql
CREATE INDEX idx_plv_keyset
  ON read.product_list_view (updated_at DESC, product_id DESC)
  WHERE deleted_at IS NULL;
```

Index chỉ cover các row chưa xóa, giảm kích thước index so với full index.

## So sánh với offset pagination cũ

| | Offset (cũ) | Keyset (mới) |
|---|---|---|
| Trang 1 | ~1ms | ~1ms |
| Trang 500 (10K rows skipped) | ~200ms+ | ~1ms |
| Trang 5000 (100K rows skipped) | timeout | ~1ms |
| Jump to page N | có | không (dùng filter thay thế) |
| Total count | có | không (dùng `hasMore`) |
| Concurrent-safe | không | có |
