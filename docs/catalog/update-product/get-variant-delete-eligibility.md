# Get Variant Delete Eligibility

## Mục đích

Kiểm tra trước khi xóa một variant: trả về thông tin về reservation và tồn kho để frontend hiển thị cảnh báo phù hợp. Đây là bước UX convenience — handler của `UpdateProductVariants` cũng tự guard lại khi thực sự xóa.

## Endpoint

```
GET /api/v1/catalog/products/{productId}/variants/{variantId}/delete-eligibility
Authorization: Bearer <token>
```

**Response:** `200 OK`

```json
{
  "canDelete": true,
  "hasActiveReservations": false,
  "hasInventoryStock": true,
  "inventoryQuantity": 12,
  "blockReason": null
}
```

| Field | Mô tả |
|---|---|
| `canDelete` | `true` nếu variant có thể xóa |
| `hasActiveReservations` | Có reservation đang active |
| `hasInventoryStock` | Còn hàng trong kho |
| `inventoryQuantity` | Số lượng tồn kho hiện tại |
| `blockReason` | Lý do không thể xóa (null nếu `canDelete = true`) |

## Logic kiểm tra

1. Variant phải tồn tại và thuộc product → 404 nếu không
2. Nếu là variant active cuối cùng → `canDelete = false`, `blockReason = "Cannot delete the last active variant"`
3. Gọi `IInventoryServiceClient.GetVariantInventoryStatusAsync`:
   - Unavailable → `canDelete = false`, `blockReason = "Cannot confirm reservation state — inventory service unavailable"`
   - `HasActiveReservations = true` → `canDelete = false`
4. Không có reservation → `canDelete = true`

## Lỗi có thể trả về

| Code | HTTP | Mô tả |
|---|---|---|
| `PRODUCT_NOT_FOUND` | 404 | ProductId không tồn tại |
| `VARIANT_NOT_FOUND` | 404 | VariantId không tồn tại hoặc đã bị xóa |

## Lưu ý

Query này không phải hard guard — `UpdateProductVariants` handler cũng tự check reservation khi thực sự xóa. Dùng query này để cải thiện UX (hiển thị cảnh báo trước khi user nhấn Save).
