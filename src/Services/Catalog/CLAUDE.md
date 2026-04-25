# Catalog Service

.NET 10 — Clean Architecture, Carter, MediatR (CQRS), EF Core + PostgreSQL, Transactional Outbox.

Port: **5290** | DB: `urbanx_catalog` | Status: **Active**

---

## Projects

| Project | Responsibility |
|---|---|
| `UrbanX.Catalog.Domain` | Entities, value objects, domain exceptions, repository interfaces |
| `UrbanX.Catalog.Application` | Commands, handlers, validators, error codes, MediatR behavior |
| `UrbanX.Catalog.Persistence` | EF Core DbContext, entity configs, repos, migrations, seeding |
| `UrbanX.Catalog.API` | Carter modules, HTTP endpoints, Trust-Gateway user context middleware, OpenAPI, Program.cs |
| `UrbanX.Catalog.Infrastructure` | Empty placeholder |

**Dependency order:** Domain ← Persistence ← Application ← API

---

## Domain

### Entities

**Product** — root aggregate
- `Guid Id`, `string Sku` (unique), `string Name`, `string Slug` (unique, SEO)
- `string? Description`, `string? ShortDescription` (max 500)
- `Guid? CategoryId / BrandId` — FKs; `string? CategoryName / BrandName` — denormalized for query speed
- `decimal BasePrice` (18,2), `Guid SellerId`, `string SellerName`
- `string Status` — values: `DRAFT | ACTIVE | INACTIVE | DELETED`
- `ProductDimensions? Dimensions` — jsonb; `List<string> Tags` — PostgreSQL `text[]`
- `int RowVersion` — optimistic concurrency token
- `DateTimeOffset? DeletedAt` — soft delete marker
- Navigation: `Category?`, `Brand?`, `List<ProductVariant> Variants`, `List<ProductImage> Images`
- Factory: `Product.Create(...)` builds entire graph in one call
- Mutators: `ApplyEdit()`, `AddVariant(spec, galleryImages, utc)`, `MarkAsDeleted()`

**ProductVariant** — child of Product
- `Guid Id`, `Guid ProductId` (cascade), `string Sku` (unique)
- `decimal Price` (18,2), `decimal? CompareAtPrice`, `bool IsActive`
- `int RowVersion`, `DateTimeOffset? DeletedAt` — soft delete
- Navigation: `ICollection<VariantAttributeValue>`
- Mutators: `SetSku()`, `SetName()`, `SetPrice()`, `SetImageUrl()`, `SetIsActive()`, `SetBarcode()`, `MarkSoftDeleted()`

**Category** — self-referential hierarchy
- `Guid? ParentId` (restrict), `string Slug` (unique)
- `string? Path` — materialized path (e.g., `/electronics/phones`)
- `int Depth` — for subtree queries
- PostgreSQL trigram GIN index on `Path` for similarity search

**Brand** — `Name` + `Slug` both unique

**AttributeDefinition** — scoped to a category
- `string Type` — `text | number | boolean | select`
- `bool IsVariantAttribute`
- Composite unique: `(CategoryId, Name)`

**ProductImage** — gallery item; `Guid? VariantId` (cascade)

**VariantAttributeValue** — join table `(VariantId, AttributeId)` composite PK

**VariantPriceHistory** — append-only audit; index `(VariantId, CreatedAt)`

**VariantSkuHistory** — append-only audit; index `VariantId`

### Value Objects

| Type | Contents |
|---|---|
| `ProductStatus` | Constants: `Draft, Active, Inactive, Deleted` |
| `ProductDimensions` | Record: `LengthCm?, WidthCm?, HeightCm?` — stored as jsonb |
| `AttributeValueTypes` | Constants: `Text, Number, Boolean, Select` |
| `NewProductSpecs` | Input specs for `Product.Create()`: `NewVariantSpec`, `NewProductImageSpec` |
| `ProductEditState` | Mutable DTO for bulk edits |

### Repository Interfaces (in Domain project)

**IProductRepository**
- `GetByIdAsync(Guid)` — no-tracking, excludes soft-deleted, includes Variants + AttributeValues + Images
- `GetByIdForUpdateAsync(Guid)` — tracked, for write transactions
- `SkuInUseAsync(string)` — checks both products and variants tables
- `IsSkuInUseExcludingAsync(string, Guid productId, Guid? variantId)` — for edit validation
- `SlugInUseAsync(string)` / `IsSlugInUseExcludingProductAsync(string, Guid productId)`
- `AddAsync(Product)`, `AddPriceHistoryAsync(VariantPriceHistory)`, `AddSkuHistoryAsync(VariantSkuHistory)`

**ICategoryRepository** — `ExistsAsync(Guid)`, `GetByIdAsync(Guid)`

**IBrandRepository** — `GetByIdAsync(Guid)`

**IAttributeDefinitionRepository** — `GetOrCreateAsync(categoryId, name, type, isVariant, displayOrder)` — upsert

### Domain Exceptions (`ProductExceptions.cs`)

`VariantsAreRequired`, `SkuIsRequired`, `SellerIdIsRequired`, `SellerNameIsRequired`, `ProductNameIsRequired`, `InvalidBasePrice`, `VariantSkuRequired`, `InvalidPrice`

### Helpers

`SlugHelper.ToSlug(string name)` — ASCII, lowercase, kebab-case URL slug

---

## Application

### Cross-Service Abstraction

`IInventoryServiceClient` (in `Application/Abstractions/`) — interface for checking variant inventory status.
- `GetVariantInventoryStatusAsync(Guid variantId)` → `VariantInventoryStatus?` (null = unavailable)
- `VariantInventoryStatus(int Quantity, bool HasActiveReservations)`
- Implementation TBD (caller provides via DI)

### Current Commands

#### `CreateProductCommand` → `Result<Guid>`

`[RequirePermission(Permissions.Products.Write)]`

Input fields: `Sku, Name, Slug?, Description?, ShortDescription?, CategoryId, BrandId?, BasePrice, Status?, WeightGrams?, Dimensions?, Tags?, MetaTitle?, MetaDescription?, ProductImages[], Variants[]`

`SellerId` / `SellerName` **không** ở payload — handler lấy từ `IUserContext.UserId` (gateway header `X-User-Id`).

Nested: `CreateProductVariantItem` (Sku, Name?, Price, CompareAtPrice?, ImageUrl?, Barcode?, Attributes[], GalleryImages[])

**Handler flow:**
1. Generate/normalize slug (SlugHelper if not provided)
2. Validate slug uniqueness → `CatalogErrors.SlugExists`
3. Validate product SKU uniqueness → `CatalogErrors.SkuExists`
4. Validate all variant SKUs uniqueness (including against each other)
5. Fetch Category — 404 if missing
6. Fetch Brand (if provided) — 404 if missing
7. `GetOrCreateAsync` each AttributeDefinition
8. Build `NewVariantSpec[]` + `NewProductImageSpec[]`
9. Resolve `SellerId` từ `IUserContext.UserId` (behavior đã đảm bảo authenticated)
10. `Product.Create(...)` builds full graph
11. `IProductRepository.AddAsync(product)`
12. `IOutboxWriter` writes `ProductCreatedV1` to outbox
13. Returns `Result<Guid>(product.Id)`

**Validator:** `CreateProductCommandValidator` (FluentValidation)
- Cross-field: all SKUs (product + all variants) must be unique in the request
- URL format validated for image fields
- Price: BasePrice >= 0; variant Price > 0 and <= 1,000,000,000

#### `UpdateProductBasicInfoCommand` → `Result`

`[RequirePermission(Permissions.Products.Write, MinScope = PermissionScope.Own)]`

Input: `ProductId, Name, Slug?, Description?, ShortDescription?, CategoryId?, BrandId?, BasePrice, Status?, WeightGrams?, Dimensions?, Tags?, MetaTitle?, MetaDescription?`

**Handler flow:**
1. `GetByIdForUpdateAsync` → `ProductNotFound`
2. Ownership check: nếu `IUserContext.Scope == Own` thì `product.SellerId` phải == `_userContext.UserId`, else `Forbidden`
3. Normalize slug → `IsSlugInUseExcludingProductAsync` → `SlugExists`
4. Category lookup if CategoryId changed → `CategoryNotFound`
5. Brand lookup if BrandId changed → `BrandNotFound`
6. `product.ApplyEdit(state, utcNow)`
7. Outbox: `ProductInfoUpdatedV1` (always) + `ProductStatusChangedV1` (if status changed)

#### `UpdateProductVariantsCommand` → `Result`

`[RequirePermission(Permissions.Products.Write, MinScope = PermissionScope.Own)]`

Input: `ProductId, Variants[]` — full snapshot; server diffs against DB.
- `VariantSnapshotItem(Id?, Sku, Name?, Price, CompareAtPrice?, ImageUrl?, Barcode?, IsActive, AttributeValues[], GalleryImages[])`

**Diff logic:** `Id == null` → add; `Id exists in DB` → update; `DB Id not in snapshot` → delete.

**Handler flow:**
1. Load product → `ProductNotFound`
2. Ownership check: nếu `Scope == Own` thì `product.SellerId == UserId`, else `Forbidden`
3. Re-check snapshot has ≥ 1 `IsActive=true` → `NoActiveVariant`
4. Validate all `toUpdate` IDs belong to product → `VariantNotFound`
5. Check reservations for `toDelete` via `IInventoryServiceClient` → `VariantHasActiveReservations` / `InventoryCheckUnavailable`
6. SKU uniqueness in DB for `toAdd` + `toUpdate` → `SkuExists`
7. Apply: soft-delete `toDelete`, upsert `toUpdate` (with SKU/Price history), `product.AddVariant` for `toAdd`
8. Outbox: `ProductVariantDeletedV1` / `ProductVariantUpdatedV1` / `ProductVariantAddedV1`

### Current Queries

#### `GetVariantDeleteEligibilityQuery` → `Result<VariantDeleteEligibilityResult>`

`[RequirePermission(Permissions.Products.Read, MinScope = PermissionScope.Own)]`

Input: `ProductId, VariantId`

Returns: `CanDelete, HasActiveReservations, HasInventoryStock, InventoryQuantity, BlockReason?`

**Handler flow:** verify product exists → ownership check (Scope==Own → product.SellerId == UserId, else Forbidden) → verify variant exists → check last-active-variant guard → call `IInventoryServiceClient` (partial result if unavailable)

### Error Codes (`CatalogErrors.cs`)

| Method | HTTP |
|---|---|
| `ProductNotFound(Guid)` | 404 |
| `VariantNotFound(Guid)` | 404 |
| `Forbidden()` | 403 |
| `OptimisticLock(int?)` | 409 |
| `VariantLock(Guid)` | 409 |
| `SkuExists(string)` | 400 |
| `SlugExists(string)` | 400 |
| `AttributeCombination()` | 400 |
| `VariantHasActiveReservations()` | 400 |
| `ProductHasActiveOrders()` | 400 |
| `InventoryCheckUnavailable()` | 503 |
| `NoActiveVariant()` | 400 |

`ProductErrors.cs` mirrors a subset: `NotFound, CategoryNotFound, BrandNotFound, SkuInUse, SlugInUse`.

### MediatR Behavior

`CatalogTransactionBehavior<TRequest, TResponse>` — extends `TransactionPipelineBehavior<..., CatalogDbContext>`. Wraps every command in a DB transaction; rolls back on exception.

`AuthorizationPipelineBehavior` (từ `Shared.Messaging`) reflect `[RequirePermission]` / `[RequireRole]` / `[AllowAnonymous]` trên Command/Query rồi check `IUserContext`. Failure trả `Result.Failure(AuthorizationErrors.Unauthenticated | MissingPermission | InsufficientScope)`.

### Placeholder Folders (not yet implemented)
- `Usecases/V1/Event/` — domain event handlers
- `Usecases/V2/` — future API version
- `Mappers/Product/` — AutoMapper profiles
- `Adapters/`

---

## Persistence

### `CatalogDbContext`

Extends `OutboxDbContext` (from `Shared.Outbox`). DbSets:

`Categories`, `Brands`, `AttributeDefinitions`, `Products`, `ProductVariants`, `VariantAttributeValues`, `ProductImages`, `VariantPriceHistories`, `VariantSkuHistories` + `OutboxMessages` (inherited)

### Table Names

| Entity | Table |
|---|---|
| Product | `products` |
| ProductVariant | `product_variants` |
| ProductImage | `product_images` |
| Category | `categories` |
| Brand | `brands` |
| AttributeDefinition | `attribute_definitions` |
| VariantAttributeValue | `variant_attribute_values` |
| VariantPriceHistory | `variant_price_history` |
| VariantSkuHistory | `variant_sku_history` |

### Notable EF Config

- `Products.Dimensions` — jsonb, serialized with `System.Text.Json`
- `Products.Tags` — PostgreSQL `text[]`
- `Products.RowVersion` / `ProductVariants.RowVersion` — EF concurrency tokens
- `Categories.Path` — GIN trigram index (`pg_trgm` extension)
- `VariantAttributeValues` — composite PK `(VariantId, AttributeId)`
- All PKs: `ValueGeneratedNever` (application assigns GUIDs)
- Prices: `HasPrecision(18, 2)` across all price columns

### Migrations (in `UrbanX.Catalog.Persistence/Migrations/`)

| Migration | Changes |
|---|---|
| `20260423021423_InitialCreate` | All base tables, FKs, indexes, `pg_trgm`, outbox table |
| `20260423025434_ProductRowVersionAndHistory` | RowVersion + DeletedAt on products/variants; price + SKU history tables |

Run migrations: `cd src/Services/Catalog/UrbanX.Catalog.Persistence && dotnet ef migrations add <Name>`

### Design-Time Factory

`CatalogDbContextFactory` reads `ConnectionStrings__catalogdb` env var. Fallback: `Host=localhost;Port=5432;Database=urbanx_catalog;Username=postgres;Password=postgres`

---

## API

### Endpoints (`Apis/ProductApis.cs`)

Base: `/api/v{version:apiVersion}/catalog/products`

| Method | Path | Handler | Response |
|---|---|---|---|
| `POST` | `/` | `CreateProductV1` | 201 + `Location` header |
| `PATCH` | `/{productId}` | `UpdateProductBasicInfoV1` | 204 No Content |
| `PUT` | `/{productId}/variants` | `UpdateProductVariantsV1` | 204 No Content |
| `GET` | `/{productId}/variants/{variantId}/delete-eligibility` | `GetVariantDeleteEligibilityV1` | 200 + `VariantDeleteEligibilityResult` |

### `ApiEndpoint` base class

`HandleFailure(Result)` / `ToCatalogResult(Result)` maps error codes → HTTP status:
- `AUTH_REQUIRED` → 401
- `FORBIDDEN` → 403
- `PRODUCT_NOT_FOUND`, `VARIANT_NOT_FOUND` → 404
- `OPTIMISTIC_LOCK_CONFLICT` → 409
- `INVENTORY_CHECK_UNAVAILABLE` → 503
- Everything else → 400

Errors serialized as RFC 7231 `ProblemDetails` with `extensions.errors[]`.

### API Versioning

URL-based: `/v1/`, `/v2/`. Group name format: `'v'VVV`. Substitutes version in URL automatically.

### Authentication / Authorization

**Trust-the-Gateway pattern.** Service KHÔNG verify JWT — Gateway verify, enrich `X-User-*` headers, strip Authorization trước khi forward.

- `app.UseUserContext()` middleware đọc headers, set OpenTelemetry activity tags
- `IUserContext` (scoped) đọc `X-User-Id` / `X-User-Roles` / `X-Merchant-Id` / `X-Permission-Scope` per request
- Endpoints **KHÔNG** dùng `RequireAuthorization()` — authorization qua MediatR `AuthorizationPipelineBehavior`
- Command/Query gắn `[RequirePermission(Permissions.Products.*)]` để khai báo permission required
- Production caveat: cần mTLS hoặc shared-secret giữa Gateway↔Service. Xem `docs/auth/trust-gateway-flow.md`

### Program.cs Registration Order

```
AddServiceDefaults() → AddOpenApi() → AddNpgsqlDbContext<CatalogDbContext>("catalogdb")
→ AddOutbox<CatalogDbContext>() → AddConfigMessaging() → AddMessaging()
→ AddApplication() → Carter(formUploadLimit: 50 MB)

app.UseExceptionHandler() → app.UseUserContext() → app.MapCarter()
```

Auto-runs EF migrations on startup.

---

## Integration Events

### Produced (via Outbox)

| Event | Fired by | Condition |
|---|---|---|
| `ProductCreatedV1` | `CreateProductCommandHandler` | Always |
| `ProductInfoUpdatedV1` | `UpdateProductBasicInfoCommandHandler` | Always |
| `ProductStatusChangedV1` | `UpdateProductBasicInfoCommandHandler` | Only if status changed |
| `ProductVariantDeletedV1` | `UpdateProductVariantsCommandHandler` | Per deleted variant |
| `ProductVariantUpdatedV1` | `UpdateProductVariantsCommandHandler` | Per variant with SKU/price/active changes |
| `ProductVariantAddedV1` | `UpdateProductVariantsCommandHandler` | Per new variant |

All events in `Shared.Contract.Messaging.Catalog.ProductUpdateIntegrationEvents` / `ProductIntegrationEvents`.

### Defined but not yet fired

`ProductDeletedV1`

### Snapshot DTOs (in `Shared.Contract.Dtos.Catalog`)

`ProductDimensionsSnapshot`, `ProductVariantSnapshot`, `ProductVariantAttributeSnapshot`, `ProductUpdateSnapshot`, `VariantPriceChange`, `VariantSnapshot`, `ProductImageSnapshot`

---

## Key Patterns

**Soft Delete** — Products: `Status=DELETED` + `DeletedAt`. Variants: `DeletedAt` + `IsActive=false`. All queries filter `DeletedAt == null AND Status != DELETED`.

**Optimistic Concurrency** — `RowVersion` on Product and ProductVariant. Conflict → `CatalogErrors.OptimisticLock` (409).

**Slug Auto-generation** — SlugHelper.ToSlug(Name) if caller omits slug. Unique constraint enforced at DB.

**Dynamic Attributes** — Per-category AttributeDefinitions. AttributeDefinitionRepository.GetOrCreateAsync upserts on create. Variants bind attribute values via VariantAttributeValue join table.

**Product Graph** — `Product.Create()` builds the entire aggregate (variants, images, attribute values) in memory; single `SaveChanges()` persists everything.

**Audit Trails** — VariantPriceHistory and VariantSkuHistory are append-only; never updated.

**Denormalization** — Product stores `CategoryName` and `BrandName` to avoid joins on reads.

**Health Check** — `CatalogDbContext` tagged `["ready", "db"]`, used by Aspire readiness probe.
