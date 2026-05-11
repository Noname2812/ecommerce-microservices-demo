---
name: add-query
description: Tạo CQRS Query mới (Query record + Validator + Handler + Carter GET endpoint). Tự động invoke khi user yêu cầu thêm read operation như "thêm query", "lấy danh sách", "get by id", "tìm kiếm", "implement GetX", "tạo query lấy X", hoặc bất kỳ từ khóa nào liên quan đến đọc dữ liệu (GetX, ListX, SearchX, FetchX) cho bất kỳ service nào trong UrbanX.
allowed-tools: Read, Grep, LS, Write, Edit, MultiEdit
---
# Skill: add-query

## Khi nào dùng

Khi người dùng yêu cầu thêm một **read operation** mới vào bất kỳ service nào (Catalog, Order, Payment, v.v.).

**Trigger examples:**
- "thêm query GetProduct"
- "implement GetProductsByCategory"
- "tạo query lấy danh sách sản phẩm có phân trang"
- `/add-query`

---

## Quy trình

### Bước 1 — Xác định service và feature name

Hỏi (nếu chưa rõ):
- Service nào? (Catalog, Order, Payment, ...)
- Tên query? (PascalCase, ví dụ: `GetProductById`, `GetProductsByCategory`)
- Trả về một item hay danh sách có phân trang?

### Bước 2 — Đọc context trước khi viết

**Bắt buộc đọc trước:**
- Repository interfaces liên quan: `src/Services/<Service>/<Service>.Domain/I*Repository.cs`
- File errors của service: `src/Services/<Service>/<Service>.Application/Usecases/V1/Errors/`
- Carter module của entity: `src/Services/<Service>/<Service>.API/Apis/<Entity>Apis.cs`

**Không đọc** các service khác trừ khi được yêu cầu rõ ràng.

### Bước 3 — Tạo các file theo thứ tự

Thứ tự chuẩn: Query (+ DTO + Validator trong cùng file) → Handler → API endpoint

### Bước 4 — Không build, không run

Chỉ viết file. Không chạy `dotnet build` trừ khi người dùng yêu cầu.

---

## Cấu trúc file

### File 1: Query + Validator + Response DTO (cùng một file)

**Path:** `src/Services/<Service>/<Service>.Application/Usecases/V1/Query/<Name>/<Name>Query.cs`

```csharp
using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;

namespace UrbanX.<Service>.Application.Usecases.V1.Query.<Name>;

[AllowAnonymous]  // hoặc [RequirePermission(Permissions.<Resource>.Read)]
public record <Name>Query(
    // ... input parameters (filter, sort, page)
) : IQuery<TResponse>;

public sealed class <Name>QueryValidator : AbstractValidator<<Name>Query>
{
    public <Name>QueryValidator()
    {
        // Validate format/range của input, không query DB
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.SearchTerm)
            .MaximumLength(200)
            .When(x => x.SearchTerm is not null);
    }
}

// Response DTO cùng file — record, chỉ expose field client cần
public record <Entity>Response(
    Guid Id,
    string Name,
    string Slug
    // ...
);
```

**Lưu ý:**
- Query là `record`, Validator là `sealed class`
- Response DTO đặt **cùng file** với Query (không tạo file riêng)
- Không inject dependency vào Validator — chỉ validate format, không query DB
- Đặt tên DTO phản ánh use case: `ProductDetailResponse` (detail), `ProductSummaryResponse` (list item)

---

### File 2: Handler

**Path:** `src/Services/<Service>/<Service>.Application/Usecases/V1/Query/<Name>/<Name>QueryHandler.cs`

**Case A — Single item (dùng read repository):**

```csharp
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.<Service>.Application.Usecases.V1.Errors;
using UrbanX.<Service>.Domain;

namespace UrbanX.<Service>.Application.Usecases.V1.Query.<Name>;

public sealed class <Name>QueryHandler(IProductReadRepository repo)
    : IQueryHandler<<Name>Query, <Entity>Response>
{
    public async Task<Result<<Entity>Response>> Handle(<Name>Query request, CancellationToken ct)
    {
        var view = await repo.GetByIdAsync(request.Id, ct);
        if (view is null)
            return Result.Failure<<Entity>Response>(CatalogErrors.ProductNotFound(request.Id));

        return Result.Success(new <Entity>Response(
            view.ProductId,
            view.Name,
            view.Slug
            // ...
        ));
    }
}
```

**Case B — Paginated list (dùng read repository):**

```csharp
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.<Service>.Domain;

namespace UrbanX.<Service>.Application.Usecases.V1.Query.<Name>;

public sealed class <Name>QueryHandler(IProductReadRepository repo)
    : IQueryHandler<<Name>Query, PageResult<<Entity>SummaryResponse>>
{
    public async Task<Result<PageResult<<Entity>SummaryResponse>>> Handle(
        <Name>Query request, CancellationToken ct)
    {
        var page = await repo.GetPageAsync(
            request.SellerId, request.CategoryId, request.Status,
            request.Page, request.PageSize, ct);

        var items = page.Items
            .Select(v => new <Entity>SummaryResponse(
                v.ProductId, v.Name, v.Slug, /* ... */))
            .ToList();

        return Result.Success(
            PageResult<<Entity>SummaryResponse>.Create(items, page.PageIndex, page.PageSize, page.TotalCount));
    }
}
```

**Case C — Query qua EF Core (chỉ dùng khi không có read schema):**

Với các service chưa có read schema riêng (Inventory, Identity, v.v.) hoặc query nội bộ không cần Dapper:

```csharp
using Microsoft.EntityFrameworkCore;
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.<Service>.Persistence;

namespace UrbanX.<Service>.Application.Usecases.V1.Query.<Name>;

public sealed class <Name>QueryHandler(<Service>DbContext db)
    : IQueryHandler<<Name>Query, PageResult<<Entity>Response>>
{
    public async Task<Result<PageResult<<Entity>Response>>> Handle(
        <Name>Query request, CancellationToken ct)
    {
        var baseQuery = db.<Entities>
            .AsNoTracking()
            .Where(e => e.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
            baseQuery = baseQuery.Where(e => e.Name.Contains(request.SearchTerm));

        var totalCount = await baseQuery.CountAsync(ct);

        var items = await baseQuery
            .OrderBy(e => e.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(e => new <Entity>Response(e.Id, e.Name, e.Slug))
            .ToListAsync(ct);

        return Result.Success(
            PageResult<<Entity>Response>.Create(items, request.Page, request.PageSize, totalCount));
    }
}
```

**Lưu ý:**
- Handler là `public sealed class`, dùng **primary constructor** khi có thể
- **Catalog service:** luôn dùng `IProductReadRepository` (Dapper-backed) cho read — không inject `CatalogDbContext` trực tiếp vào query handler
- **EF Core query (Case C):** luôn `AsNoTracking()`, không gọi `SaveChanges()`
- `TransactionPipelineBehavior` không bọc query — chỉ áp dụng cho commands
- Không cần `IOutboxWriter` — query không publish event

---

### File 3: Carter Endpoint (trong API project)

Thêm vào Carter module hiện có của entity, **không tạo file mới** nếu module đã tồn tại:

**Path:** `src/Services/<Service>/<Service>.API/Apis/<Entity>Apis.cs`

```csharp
// Trong AddRoutes(), thêm route vào group đã có:
group1.MapGet("/products", Get<Entity>ListV1);
group1.MapGet("/product/{id:guid}", Get<Entity>ByIdV1);
```

**Single item:**
```csharp
public static async Task<IResult> Get<Entity>ByIdV1(
    Guid id,
    [FromServices] ISender sender,
    CancellationToken cancellationToken)
{
    var result = await sender.Send(new Get<Entity>ByIdQuery(id), cancellationToken);
    return ToCatalogResult(result);
}
```

**Paginated list:**
```csharp
public static async Task<IResult> Get<Entity>ListV1(
    [FromServices] ISender sender,
    CancellationToken cancellationToken,
    [FromQuery] Guid? sellerId = null,
    [FromQuery] Guid? categoryId = null,
    [FromQuery] string? status = null,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20)
{
    var result = await sender.Send(
        new Get<Entity>ListQuery(sellerId, categoryId, status, page, pageSize),
        cancellationToken);
    return ToCatalogResult(result);
}
```

**HTTP method mapping:**
| Operation | HTTP | Params | Success response |
|---|---|---|---|
| Lấy 1 item theo id | GET `/{id}` | route `id` | 200 OK + body |
| Lấy danh sách có filter | GET `/items` | query string | 200 OK + `PageResult<T>` |
| Lấy item theo slug | GET `/by-slug/{slug}` | route param | 200 OK hoặc 404 |

**Lưu ý:**
- Dùng `ToCatalogResult(result)` — tự map success (200 OK + body) và failure (error status)
- Params là query string (`[FromQuery]`), không phải body
- `CancellationToken` phải có default value khi là query string endpoint
- KHÔNG dùng `RequireAuthorization()` — authorization qua attribute trên Query

---

## Query vs Command — Điểm khác biệt cốt lõi

| | Command | Query |
|---|---|---|
| Interface | `ICommand<T>` | `IQuery<T>` |
| Handler | `ICommandHandler<C, R>` | `IQueryHandler<Q, R>` |
| HTTP method | POST / PUT / PATCH / DELETE | GET |
| Params | `[FromBody]` | `[FromQuery]` / `[FromRoute]` |
| Transaction behavior | Có (tự động) | Không |
| SaveChanges | Không gọi trực tiếp | Không bao giờ |
| AsNoTracking | Không cần (write) | Bắt buộc nếu dùng EF Core |
| Data source (Catalog) | EF Core write schema | `IProductReadRepository` (Dapper, read schema) |
| Outbox | Có thể dùng | Không bao giờ |
| API error helper | `ToCatalogResult()` / `HandleFailure()` | `ToCatalogResult()` |
| Success response | 201 / 200 / 204 | 200 OK |

---

## Checklist trước khi xong

- [ ] Query là `record`, implement đúng `IQuery<TResponse>`
- [ ] Authorization attribute đúng: `[AllowAnonymous]` hoặc `[RequirePermission]`
- [ ] Response DTO đặt cùng file với Query
- [ ] Validator là `sealed class`, không query DB
- [ ] Handler là `sealed class`, dùng primary constructor
- [ ] Catalog queries: dùng `IProductReadRepository`, không inject `CatalogDbContext`
- [ ] EF Core queries (non-Catalog): `AsNoTracking()` cho mọi DbContext query
- [ ] Không gọi `SaveChanges()` trong handler
- [ ] Single item: trả `Result.Failure` với error cụ thể khi không tìm thấy
- [ ] List: trả `PageResult<T>.Create(items, page, pageSize, totalCount)`
- [ ] API endpoint dùng `ToCatalogResult()`
- [ ] Params là `[FromQuery]` / `[FromRoute]`, không phải `[FromBody]`
- [ ] Error codes không duplicate với codes đã có
- [ ] Doc: tạo/cập nhật `docs/<service>/<feature>.md`

---

## Ví dụ đầy đủ — GetProductById (Catalog)

### Query + Validator + Response DTO

```csharp
// src/Services/Catalog/UrbanX.Catalog.Application/Usecases/V1/Query/GetProductById/GetProductByIdQuery.cs
using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Catalog.Application.Usecases.V1.Query.GetProductById;

[AllowAnonymous]
public record GetProductByIdQuery(Guid ProductId) : IQuery<ProductDetailResponse>;

public sealed class GetProductByIdQueryValidator : AbstractValidator<GetProductByIdQuery>
{
    public GetProductByIdQueryValidator() =>
        RuleFor(x => x.ProductId).NotEmpty();
}

public record ProductDetailResponse(
    Guid Id,
    string Sku,
    string Name,
    string Slug,
    string Status,
    Guid? CategoryId,
    string? CategoryName,
    Guid? BrandId,
    string? BrandName,
    decimal BasePrice,
    string? ShortDescription,
    string? PrimaryImageUrl,
    List<string> Tags,
    List<VariantReadDto> Variants,
    string? MetaTitle,
    string? MetaDescription,
    int? WeightGrams,
    DateTimeOffset UpdatedAt);

public record VariantReadDto(
    Guid Id,
    string Sku,
    string? Name,
    decimal Price,
    decimal? CompareAtPrice,
    string? ImageUrl,
    bool IsActive,
    List<AttributeReadDto> Attributes,
    List<string> GalleryImageUrls);

public record AttributeReadDto(string Name, string Value);
```

### Handler

```csharp
// src/Services/Catalog/UrbanX.Catalog.Application/Usecases/V1/Query/GetProductById/GetProductByIdQueryHandler.cs
using System.Text.Json;
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Catalog.Application.Usecases.V1.Errors;
using UrbanX.Catalog.Domain;

namespace UrbanX.Catalog.Application.Usecases.V1.Query.GetProductById;

public sealed class GetProductByIdQueryHandler(IProductReadRepository repo)
    : IQueryHandler<GetProductByIdQuery, ProductDetailResponse>
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<Result<ProductDetailResponse>> Handle(GetProductByIdQuery request, CancellationToken ct)
    {
        var view = await repo.GetByIdAsync(request.ProductId, ct);
        if (view is null)
            return Result.Failure<ProductDetailResponse>(CatalogErrors.ProductNotFound(request.ProductId));

        var variants = string.IsNullOrEmpty(view.VariantsJson)
            ? []
            : JsonSerializer.Deserialize<List<VariantReadDto>>(view.VariantsJson, JsonOpts) ?? [];

        return Result.Success(new ProductDetailResponse(
            view.ProductId,
            view.Sku,
            view.Name,
            view.Slug,
            view.Status,
            view.CategoryId,
            view.CategoryName,
            view.BrandId,
            view.BrandName,
            view.BasePrice,
            view.ShortDescription,
            view.PrimaryImageUrl,
            view.Tags.ToList(),
            variants,
            view.MetaTitle,
            view.MetaDescription,
            view.WeightGrams,
            view.UpdatedAt));
    }
}
```

### API Endpoint

```csharp
// Thêm vào ProductApis.cs — AddRoutes()
group1.MapGet("/product/{productId:guid}", GetProductByIdV1);

public static async Task<IResult> GetProductByIdV1(
    Guid productId,
    [FromServices] ISender sender,
    CancellationToken cancellationToken)
{
    var result = await sender.Send(new GetProductByIdQuery(productId), cancellationToken);
    return ToCatalogResult(result);
}
```

---

## Adapter cho service khác

Khi implement query cho **service khác ngoài Catalog**, thay namespace và project prefix:

| Catalog | Order / Inventory / ... |
|---|---|
| `UrbanX.Catalog.Application` | `UrbanX.Order.Application` |
| `IProductReadRepository` (Dapper) | `IOrderRepository` hoặc inject `OrderDbContext` trực tiếp |
| `CatalogErrors` | `OrderErrors` (tương đương) |
| `ToCatalogResult()` | `ToOrderResult()` hoặc `HandleFailure()` |

Pattern và file structure hoàn toàn giống nhau — chỉ khác namespace, data source, và tên entity.
