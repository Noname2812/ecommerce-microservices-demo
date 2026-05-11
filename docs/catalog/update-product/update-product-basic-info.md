# Update Product Basic Info

## Mục đích

Cho phép seller cập nhật thông tin cơ bản của sản phẩm: tên, slug, mô tả, danh mục, thương hiệu, giá gốc, trạng thái, kích thước, tags, SEO metadata.

## Endpoint

```
PATCH /api/v1/catalog/products/{productId}
Authorization: Bearer <token>
```

**Request body:** `UpdateProductBasicInfoCommand`

| Field | Type | Required | Notes |
|---|---|---|---|
| `name` | string | ✓ | max 300 chars |
| `slug` | string? | | lowercase kebab-case; auto-generated from name if omitted |
| `description` | string? | | |
| `shortDescription` | string? | | max 500 chars |
| `categoryId` | Guid? | | triggers category name re-lookup if changed |
| `brandId` | Guid? | | triggers brand name re-lookup if changed |
| `basePrice` | decimal | ✓ | >= 0 |
| `status` | string? | | `DRAFT \| ACTIVE \| INACTIVE` |
| `weightGrams` | int? | | |
| `dimensions` | object? | | `{ lengthCm, widthCm, heightCm }` |
| `tags` | string[]? | | |
| `metaTitle` | string? | | max 255 chars |
| `metaDescription` | string? | | max 500 chars |

**Response:** `204 No Content`

## Lỗi có thể trả về

| Code | HTTP | Mô tả |
|---|---|---|
| `PRODUCT_NOT_FOUND` | 404 | ProductId không tồn tại |
| `SLUG_ALREADY_EXISTS` | 400 | Slug đã được dùng bởi product khác |
| `CATEGORY_NOT_FOUND` | 404 | CategoryId không tồn tại |
| `BRAND_NOT_FOUND` | 404 | BrandId không tồn tại |

## Integration Events

- **`ProductInfoUpdatedV1`** — luôn emit; chứa full product snapshot + danh sách active variants; trigger rebuild `read.product_list_view` + `read.product_detail_view`
- **`ProductStatusChangedV1`** — chỉ emit nếu `Status` thay đổi

Cả hai event đều được ghi vào Outbox trong cùng transaction.
