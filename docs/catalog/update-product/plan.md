# Plan: Update Product — Catalog Service

## Context

Feature update product gồm 2 use case tương ứng với 2 màn hình:
1. **UpdateProductBasicInfo** — chỉnh sửa thông tin cơ bản (không đụng tới variants)
2. **UpdateProductVariants** — quản lý variants của product: sửa properties, thêm, xóa

Business rules cho xóa variant:
- **Không thể xóa** nếu variant đang có active reservation (đang được order) → hard block trả về 400
- **Hiển thị cảnh báo** nếu variant có quantity > 0 trong inventory → frontend responsibility (gọi Inventory API trước khi submit), backend không block trường hợp này

---

## Event Redesign

File: `src/Shared/Shared.Contract/Messaging/Catalog/ProductUpdateEvents.cs`

### Giữ nguyên
| Event | Consumer | Lý do |
|---|---|---|
| `ProductStatusChangedV1` | Search, Inventory | Status transition là business event riêng biệt |
| `ProductDeletedV1` | Search, Inventory | Soft-delete toàn product |
| `ProductVariantDeletedV1` | Search, Inventory | Inventory cần deactivate stock record |

### Xóa (gộp vào event mới)
- `ProductVariantPriceUpdatedV1` → gộp vào `ProductVariantUpdatedV1`
- `ProductVariantSkuChangedV1` → gộp vào `ProductVariantUpdatedV1`
- `ProductVariantDisabledV1` → gộp vào `ProductVariantUpdatedV1`

### Đổi tên + sửa
**`ProductCatalogUpdatedV1` → `ProductInfoUpdatedV1`:**
```csharp
public record ProductInfoUpdatedV1(
    Guid ProductId,
    Guid SellerId,
    ProductDtos.ProductUpdateSnapshot Snapshot,
    IReadOnlyList<ProductDtos.ProductVariantSnapshot> ActiveVariants  // thêm mới — search cần re-index đầy đủ
) : IntegrationEventBase
{
    public override string Source => "catalog-service";
}
```

**`ProductVariantAddedV1`** — đổi `VariantSnapshot` → `ProductVariantSnapshot` (đầy đủ hơn: Barcode, IsActive):
```csharp
public record ProductVariantAddedV1(
    Guid ProductId,
    Guid SellerId,
    ProductDtos.ProductVariantSnapshot Variant
) : IntegrationEventBase
{
    public override string Source => "catalog-service";
}
```

### Thêm mới
**`ProductVariantUpdatedV1`** — gộp price, sku, active changes. Fields `Previous*` chỉ non-null khi giá trị tương ứng thay đổi:
```csharp
public record ProductVariantUpdatedV1(
    Guid ProductId,
    Guid SellerId,
    Guid VariantId,
    string? PreviousSku,      // non-null → Inventory cần cập nhật SKU reference
    decimal? PreviousPrice,   // non-null → read model cập nhật giá
    bool? PreviousIsActive,   // non-null → Inventory activate/deactivate
    ProductDtos.ProductVariantSnapshot Variant
) : IntegrationEventBase
{
    public override string Source => "catalog-service";
}
```

---

## UC1 — UpdateProductBasicInfo

**Endpoint:** `PATCH /api/v1/catalog/products/{productId}`  
**Response:** `204 No Content`

### Command
```csharp
public record UpdateProductBasicInfoCommand(
    Guid ProductId,        // route
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

### Handler Flow
1. `GetByIdForUpdateAsync(productId)` → `CatalogErrors.ProductNotFound` nếu null
2. Normalize slug (`SlugHelper.ToSlug` nếu null) → `IsSlugInUseExcludingProductAsync` → `CatalogErrors.SlugExists`
3. Category lookup nếu `CategoryId` thay đổi → 404 nếu không tồn tại
4. Brand lookup nếu `BrandId` thay đổi → 404 nếu không tồn tại
5. Capture `oldStatus = product.Status`
6. `product.ApplyEdit(state, utcNow)` ← method đã có sẵn trên Product
7. `IOutboxWriter.WriteAsync(ProductInfoUpdatedV1)` — bao gồm active variants snapshot để search re-index
8. Nếu `oldStatus != product.Status`: `IOutboxWriter.WriteAsync(ProductStatusChangedV1)`

### Validator
- `Name`: NotEmpty, MaxLength 300
- `BasePrice`: `>= 0`
- `Slug`: regex format nếu có

### Events emitted
| Event | Điều kiện |
|---|---|
| `ProductInfoUpdatedV1` | Luôn |
| `ProductStatusChangedV1` | Nếu Status thay đổi |

---

## Query — GetVariantDeleteEligibility

**Endpoint:** `GET /api/v1/catalog/products/{productId}/variants/{variantId}/delete-eligibility`  
**Response:** `200 OK` với body eligibility info  
**Mục đích:** Frontend gọi khi người dùng nhấn nút xóa để hiển thị cảnh báo phù hợp TRƯỚC khi submit.

```csharp
public record GetVariantDeleteEligibilityQuery(Guid ProductId, Guid VariantId) : IQuery<VariantDeleteEligibilityResult>;

public record VariantDeleteEligibilityResult(
    bool CanDelete,
    bool HasActiveReservations,   // true → không thể xóa
    bool HasInventoryStock,       // true → cần cảnh báo nhưng vẫn cho xóa
    int InventoryQuantity,
    string? BlockReason           // message hiển thị cho user nếu CanDelete = false
);
```

**Query Handler flow:**
1. Xác nhận variant tồn tại + thuộc product này → 404 nếu không
2. Xác nhận không phải variant cuối cùng còn active → `CanDelete = false, BlockReason = "..."`
3. Gọi Inventory service (IMessageRequestClient) → lấy `quantity` + `hasReservations`
   - Unavailable → trả về partial result (`HasInventoryStock = null`, cảnh báo "không thể kiểm tra")
4. Trả về `VariantDeleteEligibilityResult`

**Lưu ý:** Validation này được lặp lại trong `UpdateProductVariantsCommandHandler` để bảo toàn dữ liệu (defense in depth).

---

## UC2 — UpdateProductVariants

**Endpoint:** `PUT /api/v1/catalog/products/{productId}/variants`  
**Response:** `204 No Content`

Frontend gửi toàn bộ trạng thái variants từ màn hình (added/updated/deleted) trong một request.

### Command
```csharp
public record UpdateProductVariantsCommand(
    Guid ProductId,
    IReadOnlyList<UpdateVariantItem> VariantsToUpdate,
    IReadOnlyList<AddVariantItem> VariantsToAdd,
    IReadOnlyList<Guid> VariantIdsToDelete
) : ICommand;

public record UpdateVariantItem(
    Guid VariantId,
    string Sku,
    string? Name,
    decimal Price,
    decimal? CompareAtPrice,
    string? ImageUrl,
    string? Barcode,
    bool IsActive,
    IReadOnlyList<AttributeNameValueItem> Attributes
);

public record AddVariantItem(
    string Sku,
    string? Name,
    decimal Price,
    decimal? CompareAtPrice,
    string? ImageUrl,
    string? Barcode,
    IReadOnlyList<AttributeNameValueItem> Attributes,
    IReadOnlyList<CreateProductImageItem> GalleryImages
);
// AttributeNameValueItem tái sử dụng từ CreateProductCommand
// CreateProductImageItem tái sử dụng từ CreateProductCommand
```

### Handler Flow
1. `GetByIdForUpdateAsync(productId)` → `CatalogErrors.ProductNotFound`
2. **Validate ownership**: tất cả VariantId trong `VariantsToUpdate` + `VariantIdsToDelete` phải thuộc product này → `CatalogErrors.VariantNotFound`
3. **Validate không xóa sạch**: sau xóa + update IsActive=false, phải còn ≥ 1 variant active
4. **Check active reservation** (cho mỗi VariantId trong `VariantIdsToDelete`):
   - Gọi Inventory service qua `IMessageRequestClient` để kiểm tra reservation
   - Có reservation → `CatalogErrors.VariantHasActiveReservations` (400) — hard block
   - Inventory service unavailable → `CatalogErrors.InventoryCheckUnavailable` (503)
5. **SKU uniqueness** (DB):
   - Updated variants: `IsSkuInUseExcludingAsync(sku, productId, variantId)` → `CatalogErrors.SkuExists`
   - New variants: `IsSkuInUseExcludingAsync(sku, productId, null)` → `CatalogErrors.SkuExists`
6. **Resolve attributes**: `GetOrCreateAsync` cho attributes của updated + new variants
7. **Apply deletions**: `variant.MarkSoftDeleted(utcNow)` cho từng `VariantIdsToDelete`
8. **Apply updates**: Với từng `UpdateVariantItem`:
   - Capture `prevSku`, `prevPrice`, `prevIsActive`
   - SKU thay đổi → `AddSkuHistoryAsync(VariantSkuHistory)` + `variant.SetSku(newSku)`
   - Price thay đổi → `AddPriceHistoryAsync(VariantPriceHistory)`
   - `variant.SetName / SetPrice / SetImageUrl / SetBarcode / SetIsActive`
   - Attributes: `variant.AttributeValues.Clear()` + re-add (EF cascade delete handles removal)
9. **Apply additions**: `product.AddVariant(spec, galleryImages, utcNow)` ← domain method mới
10. **Write outbox** (sau tất cả mutations):
    - Mỗi deleted variant → `ProductVariantDeletedV1`
    - Mỗi updated variant có thay đổi → `ProductVariantUpdatedV1` (Previous* fields)
    - Mỗi new variant → `ProductVariantAddedV1`

### Validator
- `VariantsToUpdate`: Sku NotEmpty, Price `> 0 && <= 1_000_000_000`
- `VariantsToAdd`: Sku NotEmpty, Price `> 0`
- Cross-field: tất cả SKU trong `VariantsToUpdate` + `VariantsToAdd` phải unique trong request
- `VariantIdsToDelete` không được trùng với VariantId trong `VariantsToUpdate`

### Events emitted
| Event | Điều kiện | Consumer |
|---|---|---|
| `ProductVariantDeletedV1` | Mỗi deleted variant | Search (re-index), Inventory (deactivate stock) |
| `ProductVariantUpdatedV1` | Mỗi updated variant có thay đổi | Search (re-index), Inventory (nếu SKU/IsActive đổi) |
| `ProductVariantAddedV1` | Mỗi new variant | Search (re-index), Inventory (tạo stock record mới) |

---

## Domain Changes

### Product.cs — thêm `AddVariant`
File: `src/Services/Catalog/UrbanX.Catalog.Domain/Models/Product.cs`
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

### ProductVariant.cs — thêm `SetBarcode`
File: `src/Services/Catalog/UrbanX.Catalog.Domain/Models/ProductVariant.cs`
```csharp
public void SetBarcode(string? barcode) => Barcode = barcode;
```

---

## API Routes (ProductApis.cs)

```csharp
// PATCH /api/v1/catalog/products/{productId}
group1.MapPatch("/{productId:guid}", UpdateProductBasicInfoV1).RequireAuthorization();

// PUT /api/v1/catalog/products/{productId}/variants
group1.MapPut("/{productId:guid}/variants", UpdateProductVariantsV1).RequireAuthorization();
```

---

## Files tổng hợp

**Tạo mới (7 files):**
```
Application/Usecases/V1/Command/UpdateProductBasicInfo/
  ├── UpdateProductBasicInfoCommand.cs
  ├── UpdateProductBasicInfoCommandHandler.cs
  └── UpdateProductBasicInfoCommandValidator.cs
Application/Usecases/V1/Command/UpdateProductVariants/
  ├── UpdateProductVariantsCommand.cs
  ├── UpdateProductVariantsCommandHandler.cs
  └── UpdateProductVariantsCommandValidator.cs
docs/catalog/update-product.md
```

**Sửa (5 files):**
```
src/Shared/Shared.Contract/Messaging/Catalog/ProductUpdateEvents.cs  ← event redesign
src/Shared/Shared.Contract/Dtos/Catalog/ProductDtos.cs               ← nếu cần thêm SellerId vào VariantSnapshot
src/Services/Catalog/UrbanX.Catalog.Domain/Models/Product.cs         ← AddVariant()
src/Services/Catalog/UrbanX.Catalog.Domain/Models/ProductVariant.cs  ← SetBarcode()
src/Services/Catalog/UrbanX.Catalog.API/Apis/ProductApis.cs          ← 2 routes mới
```

---

## Verification

```bash
dotnet build UrbanX.sln
dotnet test tests/UrbanX.Services.Catalog.UnitTests/UrbanX.Services.Catalog.UnitTests.csproj
```

**Test cases cần viết:**

`UpdateProductBasicInfoCommandHandlerTests`:
- Happy path → `ProductInfoUpdatedV1` emit với ActiveVariants
- Status thay đổi → emit thêm `ProductStatusChangedV1`
- Product not found → 404
- Slug trùng → `SlugExists`

`UpdateProductVariantsCommandHandlerTests`:
- Happy path (add + update + delete cùng lúc)
- Xóa variant có active reservation → `VariantHasActiveReservations` (400)
- Inventory service unavailable → `InventoryCheckUnavailable` (503)
- Variant not found trong product → `VariantNotFound`
- Xóa variant cuối cùng còn active → validation error
- Duplicate SKU trong request → validation error
- SKU trùng DB → `SkuExists`
- SKU đổi → `AddSkuHistoryAsync` được gọi, `ProductVariantUpdatedV1.PreviousSku` non-null
- Price đổi → `AddPriceHistoryAsync` được gọi, `ProductVariantUpdatedV1.PreviousPrice` non-null
- IsActive đổi → `ProductVariantUpdatedV1.PreviousIsActive` non-null
