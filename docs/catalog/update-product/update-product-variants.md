# Update Product Variants

## Mục đích

Cập nhật toàn bộ danh sách variants của một sản phẩm theo kiểu snapshot — client gửi trạng thái cuối mong muốn, server tự tính diff và apply.

## Endpoint

```
PUT /api/v1/catalog/products/{productId}/variants
Authorization: Bearer <token>
```

**Request body:** `UpdateProductVariantsCommand`

```json
{
  "variants": [
    {
      "id": "guid-hoặc-null",
      "sku": "SKU-001",
      "name": "Đỏ / M",
      "price": 250000,
      "compareAtPrice": null,
      "imageUrl": null,
      "barcode": null,
      "isActive": true,
      "attributeValues": [{ "attributeDefinitionId": "guid", "value": "Red" }],
      "galleryImages": []
    }
  ]
}
```

**Response:** `204 No Content`

## Diff logic

| Trường hợp | Hành động |
|---|---|
| `id == null` | Tạo mới (toAdd) |
| `id` tồn tại trong DB | Cập nhật (toUpdate) |
| DB variant có `id` không có trong snapshot | Xóa mềm (toDelete) |

## Business rules

- Snapshot phải có ít nhất 1 item `isActive = true`
- Tất cả SKU trong snapshot phải unique (validator + DB check)
- Variants trong `toDelete` phải không có active reservation → kiểm tra qua `IInventoryServiceClient`

## Lỗi có thể trả về

| Code | HTTP | Mô tả |
|---|---|---|
| `PRODUCT_NOT_FOUND` | 404 | ProductId không tồn tại |
| `VARIANT_NOT_FOUND` | 404 | `id` trong toUpdate không thuộc product |
| `NO_ACTIVE_VARIANT` | 400 | Snapshot không có variant nào IsActive |
| `VARIANT_HAS_ACTIVE_ORDERS` | 400 | Variant bị xóa đang có reservation |
| `INVENTORY_CHECK_UNAVAILABLE` | 503 | Không thể gọi Inventory service |
| `SKU_ALREADY_EXISTS` | 400 | SKU đã dùng trong DB |

## Integration Events (qua Outbox)

| Event | Điều kiện |
|---|---|
| `ProductVariantDeletedV1` | Mỗi variant trong toDelete |
| `ProductVariantUpdatedV1` | Mỗi variant trong toUpdate có thay đổi SKU/Price/IsActive; `Previous*` non-null nếu field tương ứng đổi |
| `ProductVariantAddedV1` | Mỗi variant trong toAdd |

## Audit trails

- SKU đổi → ghi `VariantSkuHistory` (bảng `variant_sku_history`)
- Price đổi → ghi `VariantPriceHistory` (bảng `variant_price_history`)
