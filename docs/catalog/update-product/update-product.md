# Update Product — Catalog Service

## Tổng quan

Feature cập nhật product gồm **2 use case** tương ứng với 2 màn hình riêng biệt, cùng 1 query endpoint phục vụ UX xóa variant:

| Use Case | Endpoint | Mô tả |
|---|---|---|
| UpdateProductBasicInfo | `PATCH /api/v1/catalog/products/{productId}` | Chỉnh sửa thông tin cơ bản |
| UpdateProductVariants | `PUT /api/v1/catalog/products/{productId}/variants` | Quản lý variants (thêm/sửa/xóa) |
| GetVariantDeleteEligibility | `GET /api/v1/catalog/products/{productId}/variants/{variantId}/delete-eligibility` | Kiểm tra điều kiện xóa variant |

---

## Business Rules

### Xóa variant
- **Không thể xóa** nếu variant đang có **active reservation** trong Inventory (đang được order) → trả về `400 VariantHasActiveReservations`
- **Không thể xóa** nếu đây là **variant active cuối cùng** của product
- **Hiện cảnh báo** nếu variant có `quantity > 0` trong Inventory → vẫn cho phép xóa sau khi user xác nhận
- Frontend gọi `GetVariantDeleteEligibility` trước khi hiện dialog xóa để lấy thông tin cảnh báo
- Backend **lặp lại** cùng validation trong handler để bảo toàn dữ liệu (defense in depth)

### SKU
- SKU phải unique trong toàn hệ thống (cả products + variants)
- Khi SKU thay đổi: ghi audit vào `variant_sku_history`

### Price
- Variant price phải `> 0` và `<= 1,000,000,000`
- Khi price thay đổi: ghi audit vào `variant_price_history`

### Slug
- Auto-generate từ Name nếu không truyền vào
- Phải unique trong toàn bộ products

---

## API Contracts

### 1. UpdateProductBasicInfo

```
PATCH /api/v1/catalog/products/{productId}
Authorization: Bearer <token>
Content-Type: application/json
```

**Request body:**
```json
{
  "name": "Áo thun nam basic",
  "slug": "ao-thun-nam-basic",
  "description": "Áo thun cotton 100%",
  "shortDescription": "Cotton cao cấp",
  "categoryId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "brandId": "3fa85f64-5717-4562-b3fc-2c963f66afa7",
  "basePrice": 199000,
  "status": "ACTIVE",
  "weightGrams": 200,
  "dimensions": { "lengthCm": 30, "widthCm": 20, "heightCm": 2 },
  "tags": ["thun", "nam", "basic"],
  "metaTitle": "Áo thun nam basic - UrbanX",
  "metaDescription": "Mua áo thun nam basic chất lượng cao"
}
```

**Responses:**
- `204 No Content` — thành công
- `400` — validation error (slug trùng, giá âm, ...)
- `404` — product không tồn tại
- `401` — chưa xác thực

---

### 2. GetVariantDeleteEligibility

```
GET /api/v1/catalog/products/{productId}/variants/{variantId}/delete-eligibility
Authorization: Bearer <token>
```

**Response `200 OK`:**
```json
{
  "canDelete": false,
  "hasActiveReservations": true,
  "hasInventoryStock": true,
  "inventoryQuantity": 15,
  "blockReason": "Variant đang có đơn hàng chờ xử lý, không thể xóa."
}
```

```json
{
  "canDelete": true,
  "hasActiveReservations": false,
  "hasInventoryStock": true,
  "inventoryQuantity": 8,
  "blockReason": null
}
```
→ `canDelete: true` + `hasInventoryStock: true` → Frontend hiện cảnh báo "Còn 8 sản phẩm trong kho, bạn có chắc muốn xóa?"

**Responses:**
- `200 OK` — luôn trả về (kể cả khi Inventory unavailable, `inventoryQuantity` sẽ là -1)
- `404` — variant không tồn tại hoặc không thuộc product này

---

### 3. UpdateProductVariants

```
PUT /api/v1/catalog/products/{productId}/variants
Authorization: Bearer <token>
Content-Type: application/json
```

**Request body:**
```json
{
  "variantsToUpdate": [
    {
      "variantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "sku": "SKU-001-RED-L",
      "name": "Đỏ - L",
      "price": 199000,
      "compareAtPrice": 250000,
      "imageUrl": "https://cdn.example.com/red-l.jpg",
      "barcode": "8938500123456",
      "isActive": true,
      "attributes": [
        { "name": "Màu sắc", "value": "Đỏ" },
        { "name": "Size", "value": "L" }
      ]
    }
  ],
  "variantsToAdd": [
    {
      "sku": "SKU-001-BLUE-M",
      "name": "Xanh - M",
      "price": 199000,
      "compareAtPrice": null,
      "imageUrl": null,
      "barcode": null,
      "attributes": [
        { "name": "Màu sắc", "value": "Xanh" },
        { "name": "Size", "value": "M" }
      ],
      "galleryImages": []
    }
  ],
  "variantIdsToDelete": [
    "3fa85f64-5717-4562-b3fc-2c963f66afa9"
  ]
}
```

**Responses:**
- `204 No Content` — thành công
- `400 VariantHasActiveReservations` — variant đang được order, không thể xóa
- `400 SkuExists` — SKU đã tồn tại trong hệ thống
- `404` — product/variant không tồn tại
- `503 InventoryCheckUnavailable` — không thể kiểm tra reservation (Inventory service down)

---

## Event Flow

### UC1 — UpdateProductBasicInfo

```
PATCH /products/{id}
        │
        ▼
[UpdateProductBasicInfoCommandHandler]
        │
        ├── product.ApplyEdit(state, utcNow)
        │
        ├── Outbox: ProductInfoUpdatedV1
        │         └─ Consumer: Search Service → re-index product document
        │
        └── (nếu status thay đổi)
            Outbox: ProductStatusChangedV1
                  └─ Consumer: Search Service (deactivate/activate)
                             Inventory Service (freeze/unfreeze stock)
```

### UC2 — UpdateProductVariants

```
PUT /products/{id}/variants
        │
        ▼
[UpdateProductVariantsCommandHandler]
        │
        ├── Check reservations (Inventory RPC) cho variants cần xóa
        │
        ├── variant.MarkSoftDeleted()
        │   └── Outbox: ProductVariantDeletedV1
        │             └─ Consumer: Search (re-index), Inventory (deactivate stock)
        │
        ├── variant.SetSku / SetPrice / SetIsActive / ...
        │   ├── (SKU đổi) → VariantSkuHistory + AddSkuHistoryAsync
        │   ├── (Price đổi) → VariantPriceHistory + AddPriceHistoryAsync
        │   └── Outbox: ProductVariantUpdatedV1 { PreviousSku?, PreviousPrice?, PreviousIsActive? }
        │             └─ Consumer: Search (re-index), Inventory (update nếu SKU/IsActive đổi)
        │
        └── product.AddVariant(spec, images, utcNow)
            └── Outbox: ProductVariantAddedV1
                      └─ Consumer: Search (re-index), Inventory (tạo stock record mới)
```

---

## Event Contracts

Tất cả events được publish qua **Transactional Outbox** (ghi cùng transaction với data mutations), relay worker sẽ publish lên RabbitMQ sau đó.

### ProductInfoUpdatedV1
```
Khi nào: Mỗi lần UpdateProductBasicInfo thành công
Consumer: Search Service (re-index toàn bộ product document)
Payload:
  - ProductId, SellerId
  - Snapshot (basic info)
  - ActiveVariants[] (để Search re-index đầy đủ không cần query lại)
```

### ProductStatusChangedV1
```
Khi nào: Status thay đổi trong UpdateProductBasicInfo
Consumer: Search Service, Inventory Service
Payload:
  - ProductId, OldStatus, NewStatus, Reason, AffectedVariantIds
```

### ProductVariantAddedV1
```
Khi nào: Variant mới được thêm vào trong UpdateProductVariants
Consumer: Search Service, Inventory Service
Payload:
  - ProductId, SellerId
  - Variant (full snapshot: Sku, Name, Price, Attributes, IsActive, ...)
Inventory action: tạo mới inventory record cho variant này
```

### ProductVariantUpdatedV1
```
Khi nào: Variant bị sửa trong UpdateProductVariants (chỉ emit nếu có thay đổi)
Consumer: Search Service, Inventory Service
Payload:
  - ProductId, SellerId, VariantId
  - PreviousSku (non-null nếu SKU đổi) → Inventory cập nhật reference
  - PreviousPrice (non-null nếu price đổi)
  - PreviousIsActive (non-null nếu active status đổi) → Inventory activate/deactivate
  - Variant (full snapshot sau update)
```

### ProductVariantDeletedV1
```
Khi nào: Variant bị xóa mềm trong UpdateProductVariants
Consumer: Search Service, Inventory Service
Payload:
  - ProductId, VariantId, Sku
Inventory action: deactivate stock record
```

---

## Cross-Service Dependencies

| Dependency | Cách giao tiếp | Fallback |
|---|---|---|
| Inventory Service (check reservation) | `IMessageRequestClient` (RPC over RabbitMQ) | `503 InventoryCheckUnavailable` |
| Search Service (re-index) | Async via Outbox + RabbitMQ | Eventual consistency — không block |

**Lưu ý:** Inventory Service chưa được implement. Khi tích hợp cần implement consumer cho các events trên và expose RPC endpoint cho reservation check.

---

## Database Audit Trails

| Bảng | Ghi khi nào | Mục đích |
|---|---|---|
| `variant_price_history` | Price thay đổi trong UpdateProductVariants | Audit lịch sử giá |
| `variant_sku_history` | SKU thay đổi trong UpdateProductVariants | Audit lịch sử SKU |

Cả hai bảng là **append-only** — không bao giờ UPDATE hay DELETE.

---

## File Structure

```
Application/Usecases/V1/
├── Command/
│   ├── UpdateProductBasicInfo/
│   │   ├── UpdateProductBasicInfoCommand.cs
│   │   ├── UpdateProductBasicInfoCommandHandler.cs
│   │   └── UpdateProductBasicInfoCommandValidator.cs
│   └── UpdateProductVariants/
│       ├── UpdateProductVariantsCommand.cs
│       ├── UpdateProductVariantsCommandHandler.cs
│       └── UpdateProductVariantsCommandValidator.cs
└── Query/
    └── GetVariantDeleteEligibility/
        ├── GetVariantDeleteEligibilityQuery.cs
        └── GetVariantDeleteEligibilityQueryHandler.cs
```
