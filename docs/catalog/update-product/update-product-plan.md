## Plan: Update Product — Catalog Service

### Mục tiêu
Triển khai 2 use case cập nhật product: **UpdateProductBasicInfo** (thông tin cơ bản) và **UpdateProductVariants** (quản lý variants: thêm / sửa / xóa), kèm query **GetVariantDeleteEligibility** để frontend kiểm tra trước khi xóa variant.

### Hướng đã chọn
Tách thành 2 command riêng biệt (BasicInfo + Variants) vì 2 màn hình có payload và business rule hoàn toàn khác nhau; tránh một command God-object khó validate và khó test. Redesign integration events để gộp các event lẻ thành `ProductVariantUpdatedV1`, giảm số lượng consumer phải xử lý.

---

### Các bước thực hiện

1. **Shared.Contract — Event Redesign** — Sửa `ProductUpdateEvents.cs`: đổi tên `ProductCatalogUpdatedV1` → `ProductInfoUpdatedV1` (thêm `ActiveVariants`), thêm `ProductVariantUpdatedV1` (gộp price/sku/active changes), cập nhật `ProductVariantAddedV1` dùng `ProductVariantSnapshot`; xóa `ProductVariantPriceUpdatedV1`, `ProductVariantSkuChangedV1`, `ProductVariantDisabledV1`.
   (không có skill riêng — follow pattern hiện có)

2. **Domain** — Thêm `AddVariant(spec, galleryImages, utc)` vào `Product.cs`; thêm `SetBarcode(barcode)` vào `ProductVariant.cs`.
   (không có skill riêng — follow Clean Architecture standard)

3. **Application — Command + Validator + Handler (UC1: UpdateProductBasicInfo)** — `UpdateProductBasicInfoCommand` + Validator + Handler; flow: load product → validate slug unique → category/brand lookup → `product.ApplyEdit()` → write outbox `ProductInfoUpdatedV1` (+ `ProductStatusChangedV1` nếu status đổi).
   🔧 Skill: `.claude/skills/add-command/SKILL.md`

4. **Application — Command + Validator + Handler (UC2: UpdateProductVariants)** — `UpdateProductVariantsCommand` nhận snapshot toàn bộ variants; server tự diff (null Id = add, có Id = update, Id không có trong snapshot = delete). Validator + Handler; flow: diff → validate ownership → validate ≥ 1 active → check reservation (hard block) → validate SKU unique → apply changes → write outbox `ProductVariantDeletedV1` / `ProductVariantUpdatedV1` / `ProductVariantAddedV1`.
   🔧 Skill: `.claude/skills/add-command/SKILL.md`

5. **Application — Query + Handler (GetVariantDeleteEligibility)** — Query kiểm tra trước khi xóa: xác nhận variant tồn tại, không phải variant active cuối cùng, gọi Inventory service lấy quantity + reservation status → trả về `VariantDeleteEligibilityResult`.
   🔧 Skill: `.claude/skills/add-query/SKILL.md`

6. **Persistence** — Không cần thay đổi schema. Kiểm tra repo methods: `GetByIdForUpdateAsync`, `IsSlugInUseExcludingProductAsync`, `IsSkuInUseExcludingAsync`, `AddSkuHistoryAsync`, `AddPriceHistoryAsync`.
   (không có skill riêng — follow pattern hiện có)

7. **Migration** — Không cần migration (không thay đổi DB schema).

8. **API** — Thêm 3 endpoints vào `ProductApis.cs`: `PATCH /{productId}` (BasicInfo), `PUT /{productId}/variants` (Variants), `GET /{productId}/variants/{variantId}/delete-eligibility`.
   🔧 Skill: `.claude/skills/add-command/SKILL.md` (xem phần API endpoint)

9. **Unit Test** — `UpdateProductBasicInfoCommandHandlerTests` (4 cases) + `UpdateProductVariantsCommandHandlerTests` (9 cases).
   🔧 Skill: `.claude/skills/unit-test-writer/SKILL.md`

10. **Docs** — Tạo `docs/catalog/update-product/plan.md` (file này).

---

### Files cần tạo mới

```
src/Services/Catalog/UrbanX.Catalog.Application/Usecases/V1/Command/UpdateProductBasicInfo/
  ├── UpdateProductBasicInfoCommand.cs
  ├── UpdateProductBasicInfoCommandHandler.cs
  └── UpdateProductBasicInfoCommandValidator.cs
src/Services/Catalog/UrbanX.Catalog.Application/Usecases/V1/Command/UpdateProductVariants/
  ├── UpdateProductVariantsCommand.cs
  ├── UpdateProductVariantsCommandHandler.cs
  └── UpdateProductVariantsCommandValidator.cs
src/Services/Catalog/UrbanX.Catalog.Application/Usecases/V1/Query/GetVariantDeleteEligibility/
  ├── GetVariantDeleteEligibilityQuery.cs
  └── GetVariantDeleteEligibilityQueryHandler.cs
```

### Files cần chỉnh sửa

- `src/Shared/Shared.Contract/Messaging/Catalog/ProductUpdateEvents.cs` — event redesign (đổi tên, gộp, thêm mới)
- `src/Shared/Shared.Contract/Dtos/Catalog/ProductDtos.cs` — kiểm tra `ProductVariantSnapshot` có `Barcode`, `IsActive`
- `src/Services/Catalog/UrbanX.Catalog.Domain/Models/Product.cs` — thêm `AddVariant()`
- `src/Services/Catalog/UrbanX.Catalog.Domain/Models/ProductVariant.cs` — thêm `SetBarcode()`
- `src/Services/Catalog/UrbanX.Catalog.API/Apis/ProductApis.cs` — 3 routes mới

---

### Migration

- [ ] Không cần migration

---

### Integration events

**Publish** (từ Catalog service):

| Event | Điều kiện | Consumer |
|---|---|---|
| `ProductInfoUpdatedV1` | UC1: luôn emit | Search (re-index), Inventory |
| `ProductStatusChangedV1` | UC1: nếu status thay đổi | Search, Inventory |
| `ProductVariantDeletedV1` | UC2: mỗi variant bị xóa | Search (re-index), Inventory (deactivate stock) |
| `ProductVariantUpdatedV1` | UC2: mỗi variant có thay đổi | Search (re-index), Inventory (nếu SKU/IsActive đổi) |
| `ProductVariantAddedV1` | UC2: mỗi variant mới | Search (re-index), Inventory (tạo stock record) |

**Consume** (Catalog gọi Inventory qua `IMessageRequestClient`):
- Request/Response: kiểm tra active reservation trước khi xóa variant (UC2 handler + GetVariantDeleteEligibility query)

---

### Rủi ro / Lưu ý

- **Breaking change events**: Xóa `ProductVariantPriceUpdatedV1`, `ProductVariantSkuChangedV1`, `ProductVariantDisabledV1` — Search + Inventory consumer phải cập nhật để consume `ProductVariantUpdatedV1` thay thế.
- **Inventory service unavailable**: UC2 handler trả `CatalogErrors.InventoryCheckUnavailable` (503) nếu không gọi được Inventory để check reservation. Query eligibility trả partial result.
- **Defense in depth**: Eligibility query và UC2 handler đều check reservation — query là UX convenience, handler là hard guard.
- **`product.AddVariant()`**: Method mới trên domain — cần kiểm tra `ProductVariant.Create()` đã có sẵn hay chưa.
- **Attributes**: UC2 dùng `GetOrCreateAsync` để resolve attributes — đảm bảo idempotent nếu attribute đã tồn tại.

---

### Docs cần cập nhật

- `docs/catalog/update-product/plan.md` (file này)

---

## Spec chi tiết

### Event Redesign — `ProductUpdateEvents.cs`

**Giữ nguyên:**
- `ProductStatusChangedV1` — status transition là business event riêng
- `ProductDeletedV1` — soft-delete toàn product
- `ProductVariantDeletedV1` — Inventory cần deactivate stock record

**Xóa** (gộp vào `ProductVariantUpdatedV1`):
- `ProductVariantPriceUpdatedV1`
- `ProductVariantSkuChangedV1`
- `ProductVariantDisabledV1`

**Đổi tên + sửa:**

```csharp
// ProductCatalogUpdatedV1 → ProductInfoUpdatedV1
public record ProductInfoUpdatedV1(
    Guid ProductId,
    Guid SellerId,
    ProductDtos.ProductUpdateSnapshot Snapshot,
    IReadOnlyList<ProductDtos.ProductVariantSnapshot> ActiveVariants  // search re-index đầy đủ
) : IntegrationEventBase
{
    public override string Source => "catalog-service";
}

// ProductVariantAddedV1 — đổi VariantSnapshot → ProductVariantSnapshot
public record ProductVariantAddedV1(
    Guid ProductId,
    Guid SellerId,
    ProductDtos.ProductVariantSnapshot Variant
) : IntegrationEventBase
{
    public override string Source => "catalog-service";
}
```

**Thêm mới:**

```csharp
// Previous* fields chỉ non-null khi giá trị tương ứng thay đổi
public record ProductVariantUpdatedV1(
    Guid ProductId,
    Guid SellerId,
    Guid VariantId,
    string? PreviousSku,       // non-null → Inventory cập nhật SKU reference
    decimal? PreviousPrice,    // non-null → read model cập nhật giá
    bool? PreviousIsActive,    // non-null → Inventory activate/deactivate
    ProductDtos.ProductVariantSnapshot Variant
) : IntegrationEventBase
{
    public override string Source => "catalog-service";
}
```

---

### UC1 — UpdateProductBasicInfo

**Endpoint:** `PATCH /api/v1/catalog/products/{productId}` → `204 No Content`

**Command:**
```csharp
public record UpdateProductBasicInfoCommand(
    Guid ProductId,
    string Name,
    string? Slug,
    string? Description,
    string? ShortDescription,
    Guid? CategoryId,
    Guid? BrandId,
    decimal BasePrice,
    string? Status,
    int? WeightGrams,
    ProductDimensionsInput? Dimensions,
    IReadOnlyList<string>? Tags,
    string? MetaTitle,
    string? MetaDescription
) : ICommand;
```

**Validator:** `Name` NotEmpty MaxLength(300); `BasePrice >= 0`; `Slug` regex nếu có.

**Handler flow:**
1. `GetByIdForUpdateAsync(productId)` → `CatalogErrors.ProductNotFound`
2. Normalize slug → `IsSlugInUseExcludingProductAsync` → `CatalogErrors.SlugExists`
3. Category lookup nếu `CategoryId` thay đổi → 404
4. Brand lookup nếu `BrandId` thay đổi → 404
5. Capture `oldStatus = product.Status`
6. `product.ApplyEdit(state, utcNow)`
7. `IOutboxWriter.WriteAsync(ProductInfoUpdatedV1)` — gồm active variants snapshot
8. Nếu `oldStatus != product.Status` → `IOutboxWriter.WriteAsync(ProductStatusChangedV1)`

---

### Query — GetVariantDeleteEligibility

**Endpoint:** `GET /api/v1/catalog/products/{productId}/variants/{variantId}/delete-eligibility` → `200 OK`

```csharp
public record GetVariantDeleteEligibilityQuery(Guid ProductId, Guid VariantId) : IQuery<VariantDeleteEligibilityResult>;

public record VariantDeleteEligibilityResult(
    bool CanDelete,
    bool HasActiveReservations,
    bool HasInventoryStock,
    int InventoryQuantity,
    string? BlockReason
);
```

**Handler flow:**
1. Xác nhận variant tồn tại + thuộc product → 404 nếu không
2. Xác nhận không phải variant active cuối → `CanDelete = false`
3. Gọi Inventory (IMessageRequestClient) → quantity + hasReservations
   - Unavailable → partial result, cảnh báo "không thể kiểm tra"
4. Trả về `VariantDeleteEligibilityResult`

---

### UC2 — UpdateProductVariants

**Endpoint:** `PUT /api/v1/catalog/products/{productId}/variants` → `204 No Content`

**Thiết kế:** Client gửi snapshot toàn bộ variants hiện tại trên màn hình sau khi user chỉnh sửa xong và nhấn Save. Server tự diff với DB:
- `Id == null` → tạo mới
- `Id != null` và tồn tại trong DB → upsert (update)
- Variant trong DB có `Id` không nằm trong snapshot → xóa (soft delete)

**Command:**
```csharp
public record UpdateProductVariantsCommand(
    Guid ProductId,
    IReadOnlyList<VariantSnapshotItem> Variants  // toàn bộ variants sau khi user save
) : ICommand;

public record VariantSnapshotItem(
    Guid? Id,               // null = new; non-null = existing
    string Sku,
    string? Name,
    decimal Price,
    decimal? CompareAtPrice,
    string? ImageUrl,
    string? Barcode,
    bool IsActive,
    IReadOnlyList<AttributeValueInput>? AttributeValues,
    IReadOnlyList<GalleryImageInput>? GalleryImages
);
```

**Validator:**
- `Variants` NotEmpty (không cho phép xóa hết)
- Mỗi item: `Sku` NotEmpty, `Price > 0 && <= 1_000_000_000`
- Cross-field: tất cả `Sku` trong snapshot phải unique
- Snapshot phải có ít nhất 1 item `IsActive = true`

**Diff logic (tính trước khi validate business rules):**
```
toAdd    = Variants where Id == null
toUpdate = Variants where Id != null  (all; server validates Id thuộc product)
toDelete = DB variants where Id ∉ { snapshot Ids }
```

**Handler flow:**
1. `GetByIdForUpdateAsync(productId)` → `CatalogErrors.ProductNotFound`
2. Diff: tính `toAdd` / `toUpdate` / `toDelete` từ snapshot vs DB variants
3. Validate: tất cả Id trong `toUpdate` phải thuộc product → `CatalogErrors.VariantNotFound`
4. Validate: snapshot có ≥ 1 item `IsActive = true` (validator đã chặn, handler re-check)
5. Check active reservation qua Inventory cho mỗi variant trong `toDelete`:
   - Có reservation → `CatalogErrors.VariantHasActiveReservations` (400)
   - Unavailable → `CatalogErrors.InventoryCheckUnavailable` (503)
6. SKU uniqueness DB: `IsSkuInUseExcludingAsync` cho `toAdd` + `toUpdate` → `CatalogErrors.SkuExists`
7. Resolve attributes: `GetOrCreateAsync` cho attributes của `toUpdate` + `toAdd`
8. Apply deletions: `variant.MarkSoftDeleted(utcNow)` cho `toDelete`
9. Apply updates: capture prev values → upsert fields → `AddSkuHistoryAsync` / `AddPriceHistoryAsync` nếu có thay đổi
10. Apply additions: `product.AddVariant(spec, galleryImages, utcNow)` cho `toAdd`
11. Write outbox: `ProductVariantDeletedV1` / `ProductVariantUpdatedV1` / `ProductVariantAddedV1`

---

### Domain changes

**`Product.cs` — thêm `AddVariant`:**
```csharp
public void AddVariant(
    NewVariantSpec spec,
    IReadOnlyList<NewProductImageSpec> galleryImages,
    DateTimeOffset utc)
{
    var v = ProductVariant.Create(Id, spec.Sku, spec.Name, spec.Price,
                                   spec.CompareAtPrice, spec.ImageUrl, spec.Barcode,
                                   spec.AttributeValues);
    Variants.Add(v);
    var order = 0;
    foreach (var g in galleryImages)
        Images.Add(new ProductImage { Id = Guid.NewGuid(), ProductId = Id, VariantId = v.Id,
                                       Url = g.Url, AltText = g.AltText,
                                       DisplayOrder = g.DisplayOrder != 0 ? g.DisplayOrder : order++,
                                       IsPrimary = g.IsPrimary });
    UpdatedAt = utc;
}
```

**`ProductVariant.cs` — thêm `SetBarcode`:**
```csharp
public void SetBarcode(string? barcode) => Barcode = barcode;
```

---

### Test cases cần viết

**`UpdateProductBasicInfoCommandHandlerTests`:**
- Happy path → `ProductInfoUpdatedV1` emit với `ActiveVariants`
- Status thay đổi → emit thêm `ProductStatusChangedV1`
- Product not found → `CatalogErrors.ProductNotFound`
- Slug trùng → `CatalogErrors.SlugExists`

**`UpdateProductVariantsCommandHandlerTests`:**
- Happy path: snapshot có add + update + delete → 3 loại outbox event được emit
- Snapshot omit tất cả DB variants (Id không có trong snapshot) → tất cả bị soft delete → bị chặn vì không còn active variant
- Snapshot item có Id không thuộc product → `VariantNotFound`
- Variant bị diff sang `toDelete` có active reservation → `VariantHasActiveReservations` (400)
- Inventory service unavailable khi check reservation → `InventoryCheckUnavailable` (503)
- SKU trùng DB (toAdd hoặc toUpdate) → `SkuExists`
- SKU đổi trong toUpdate → `AddSkuHistoryAsync` được gọi, `ProductVariantUpdatedV1.PreviousSku` non-null
- Price đổi trong toUpdate → `AddPriceHistoryAsync` được gọi, `ProductVariantUpdatedV1.PreviousPrice` non-null
- Snapshot chỉ có Id null items → toDelete = tất cả DB variants, toAdd = snapshot items (full replace)
