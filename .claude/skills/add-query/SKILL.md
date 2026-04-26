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

Thứ tự chuẩn: Query → Handler → API endpoint

### Bước 4 — Không build, không run

Chỉ viết file. Không chạy `dotnet build` trừ khi người dùng yêu cầu.

---

## Cấu trúc file

### File 1: Query + Validator

**Path:** `src/Services/<Service>/<Service>.Application/Usecases/V1/Query/<Name>/<Name>Query.cs`

```csharp
using FluentValidation;
using Shared.Application;
using Shared.Kernel.Primitives;

namespace UrbanX.<Service>.Application.Usecases.V1.Query;

// Query là record, implement IQuery<TResponse>
// - Trả về 1 item:   IQuery<ProductDto>
// - Trả về danh sách có phân trang: IQuery<PageResult<ProductDto>>
public record <Name>Query(
    // ... input parameters (filter, sort, page)
) : IQuery<TResponse>;

public sealed class <Name>QueryValidator : AbstractValidator<<Name>Query>
{
    public <Name>QueryValidator()
    {
        // Validate format/range của input, không query DB
        RuleFor(x => x.PageIndex).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize)
            .GreaterThanOrEqualTo(1)
            .LessThanOrEqualTo(PageResult<object>.UpperPageSize);
        RuleFor(x => x.SearchTerm)
            .MaximumLength(200)
            .When(x => x.SearchTerm is not null);
    }
}
```

**Lưu ý:**
- Query là `record`, không phải `class`
- Validator là `sealed class`
- Không inject dependency vào Validator — chỉ validate format, không query DB
- Validator cho query thường đơn giản: bound check cho pagination, length check cho search term

---

### File 2: Response DTO (nếu cần)

Tạo DTO khi response cần shape khác với Domain entity (thường là cần).

**Path:** `src/Services/<Service>/<Service>.Application/Usecases/V1/Query/<Name>/<Name>Response.cs`
hoặc dùng chung ở `Usecases/V1/Query/Dtos/`

```csharp
namespace UrbanX.<Service>.Application.Usecases.V1.Query;

public record <Entity>Dto(
    Guid Id,
    string Name,
    string Slug,
    // ... các field cần thiết cho client
);
```

**Lưu ý:**
- DTO là `record` — immutable
- Chỉ expose những field client cần, không expose internal state của entity
- Đặt tên DTO phản ánh use case (ví dụ: `ProductListItemDto` cho list, `ProductDetailDto` cho detail)

---

### File 3: Handler

**Path:** `src/Services/<Service>/<Service>.Application/Usecases/V1/Query/<Name>/<Name>QueryHandler.cs`

**Case A — Single item:**
```csharp
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.<Service>.Application.Usecases.V1.Errors;
using UrbanX.<Service>.Domain;

namespace UrbanX.<Service>.Application.Usecases.V1.Query;

public sealed class <Name>QueryHandler : IQueryHandler<<Name>Query, <Entity>Dto>
{
    private readonly IProductRepository _productRepository;

    public <Name>QueryHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Result<<Entity>Dto>> Handle(<Name>Query request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.Id, cancellationToken);
        if (product is null)
            return Result.Failure<<Entity>Dto>(CatalogErrors.ProductNotFound(request.Id));

        return Result.Success(new <Entity>Dto(
            product.Id,
            product.Name,
            product.Slug
            // ...
        ));
    }
}
```

**Case B — Paginated list (với EF Core DbContext trực tiếp):**

Dùng khi repo interface chưa có method phù hợp — query handler inject `CatalogDbContext` và dùng LINQ trực tiếp.

```csharp
using Microsoft.EntityFrameworkCore;
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.<Service>.Persistence;

namespace UrbanX.<Service>.Application.Usecases.V1.Query;

public sealed class <Name>QueryHandler : IQueryHandler<<Name>Query, PageResult<<Entity>Dto>>
{
    private readonly CatalogDbContext _db;

    public <Name>QueryHandler(CatalogDbContext db)
    {
        _db = db;
    }

    public async Task<Result<PageResult<<Entity>Dto>>> Handle(<Name>Query request, CancellationToken cancellationToken)
    {
        var baseQuery = _db.Products
            .AsNoTracking()
            .Where(p => p.DeletedAt == null);

        // Apply filters
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
            baseQuery = baseQuery.Where(p => p.Name.Contains(request.SearchTerm));

        if (request.CategoryId.HasValue)
            baseQuery = baseQuery.Where(p => p.CategoryId == request.CategoryId);

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var items = await baseQuery
            .OrderBy(p => p.Name)
            .Skip((request.PageIndex - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(p => new <Entity>Dto(p.Id, p.Name, p.Slug))
            .ToListAsync(cancellationToken);

        return Result.Success(PageResult<<Entity>Dto>.Create(items, request.PageIndex, request.PageSize, totalCount));
    }
}
```

**Lưu ý:**
- Handler là `sealed class`, implement `IQueryHandler<TQuery, TResponse>`
- **Luôn dùng `AsNoTracking()`** — query không cần change tracking
- **Không gọi `SaveChanges()`** — query chỉ đọc
- Không cần `IOutboxWriter` — query không publish event
- `CatalogTransactionBehavior` **không bọc** query — chỉ áp dụng cho commands
- Early return khi không tìm thấy entity

---

### File 4: Carter Endpoint (trong API project)

Thêm vào Carter module hiện có của entity, **không tạo file mới** nếu module đã tồn tại:

**Path:** `src/Services/<Service>/<Service>.API/Apis/<Entity>Apis.cs`

```csharp
// Trong AddRoutes(), thêm route vào group đã có:
group1.MapGet("/", Get<Entity>ListV1);
group1.MapGet("/{id:guid}", Get<Entity>ByIdV1);

// Handler cho single item:
public static async Task<IResult> Get<Entity>ByIdV1(
    [FromServices] ISender sender,
    [FromRoute] Guid id,
    CancellationToken cancellationToken)
{
    var result = await sender.Send(new Get<Entity>ByIdQuery(id), cancellationToken);
    return ToCatalogResult(result);
}

// Handler cho paginated list:
public static async Task<IResult> Get<Entity>ListV1(
    [FromServices] ISender sender,
    [FromQuery] int pageIndex = 1,
    [FromQuery] int pageSize = 10,
    [FromQuery] string? searchTerm = null,
    [FromQuery] Guid? categoryId = null,
    CancellationToken cancellationToken = default)
{
    var result = await sender.Send(
        new Get<Entity>ListQuery(pageIndex, pageSize, searchTerm, categoryId),
        cancellationToken);
    return ToCatalogResult(result);
}
```

**HTTP method mapping:**
| Operation | HTTP | Params | Success response |
|---|---|---|---|
| Lấy 1 item theo id | GET `/{id}` | route `id` | 200 OK + body |
| Lấy danh sách có filter | GET `/` | query string | 200 OK + `PageResult<T>` |
| Lấy item theo slug/code | GET `/by-slug/{slug}` | route param | 200 OK hoặc 404 |

**Lưu ý:**
- Dùng `ToCatalogResult(result)` thay vì `HandleFailure(result)` — method này tự map 404/403/409
- Params là query string (`[FromQuery]`), không phải body
- `CancellationToken` phải có default value khi là query string endpoint

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
| AsNoTracking | Không cần | Bắt buộc |
| Outbox | Có thể dùng | Không bao giờ |
| API error helper | `HandleFailure()` | `ToCatalogResult()` |
| Success response | 201 / 200 / 204 | 200 OK |

---

## Checklist trước khi xong

- [ ] Query là `record`, implement đúng `IQuery<TResponse>`
- [ ] Validator là `sealed class`, không query DB
- [ ] Handler là `sealed class`, `AsNoTracking()` cho mọi DbContext query
- [ ] Không gọi `SaveChanges()` trong handler
- [ ] Single item: trả `Result.Failure` với error cụ thể khi không tìm thấy
- [ ] List: trả `PageResult<T>.Create(items, pageIndex, pageSize, totalCount)`
- [ ] API endpoint dùng `ToCatalogResult()` (không phải `HandleFailure()`)
- [ ] Params là `[FromQuery]` / `[FromRoute]`, không phải `[FromBody]`
- [ ] Error codes không duplicate với codes đã có
- [ ] Doc: tạo/cập nhật `docs/<service>/<feature>.md`

---

## Ví dụ đầy đủ — GetProductById (Catalog)

### Query + Validator

```csharp
// src/Services/Catalog/UrbanX.Catalog.Application/Usecases/V1/Query/GetProductById/GetProductByIdQuery.cs
using Shared.Application;

namespace UrbanX.Catalog.Application.Usecases.V1.Query;

public record GetProductByIdQuery(Guid ProductId) : IQuery<ProductDetailDto>;

public sealed class GetProductByIdQueryValidator : AbstractValidator<GetProductByIdQuery>
{
    public GetProductByIdQueryValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
    }
}
```

### Response DTO

```csharp
// src/Services/Catalog/UrbanX.Catalog.Application/Usecases/V1/Query/GetProductById/ProductDetailDto.cs
namespace UrbanX.Catalog.Application.Usecases.V1.Query;

public record ProductDetailDto(
    Guid Id,
    string Sku,
    string Name,
    string Slug,
    string? Description,
    string? CategoryName,
    string? BrandName,
    decimal BasePrice,
    string Status,
    IReadOnlyList<ProductVariantDto> Variants
);

public record ProductVariantDto(
    Guid Id,
    string Sku,
    decimal Price,
    decimal? CompareAtPrice,
    bool IsActive
);
```

### Handler

```csharp
// src/Services/Catalog/UrbanX.Catalog.Application/Usecases/V1/Query/GetProductById/GetProductByIdQueryHandler.cs
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Catalog.Application.Usecases.V1.Errors;
using UrbanX.Catalog.Domain;

namespace UrbanX.Catalog.Application.Usecases.V1.Query;

public sealed class GetProductByIdQueryHandler : IQueryHandler<GetProductByIdQuery, ProductDetailDto>
{
    private readonly IProductRepository _productRepository;

    public GetProductByIdQueryHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Result<ProductDetailDto>> Handle(GetProductByIdQuery request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);
        if (product is null)
            return Result.Failure<ProductDetailDto>(CatalogErrors.ProductNotFound(request.ProductId));

        return Result.Success(new ProductDetailDto(
            product.Id,
            product.Sku,
            product.Name,
            product.Slug,
            product.Description,
            product.CategoryName,
            product.BrandName,
            product.BasePrice,
            product.Status,
            product.Variants
                .Where(v => v.DeletedAt == null)
                .Select(v => new ProductVariantDto(v.Id, v.Sku, v.Price, v.CompareAtPrice, v.IsActive))
                .ToList()
        ));
    }
}
```

### API Endpoint

```csharp
// Thêm vào ProductApis.cs — AddRoutes()
group1.MapGet("/{id:guid}", GetProductByIdV1);

// Handler method
public static async Task<IResult> GetProductByIdV1(
    [FromServices] ISender sender,
    [FromRoute] Guid id,
    CancellationToken cancellationToken)
{
    var result = await sender.Send(new GetProductByIdQuery(id), cancellationToken);
    return ToCatalogResult(result);
}
```

---

## Adapter cho service khác

Khi implement query cho **service khác ngoài Catalog**, thay namespace và project prefix:

| Catalog | Order / Payment / ... |
|---|---|
| `UrbanX.Catalog.Application` | `UrbanX.Order.Application` |
| `UrbanX.Catalog.Domain` | `UrbanX.Order.Domain` |
| `CatalogDbContext` | `OrderDbContext` (tương đương) |
| `CatalogErrors` | `OrderErrors` (tương đương) |
| `ToCatalogResult()` | `ToOrderResult()` (hoặc `HandleFailure()` nếu chưa có) |

Pattern và file structure hoàn toàn giống nhau — chỉ khác namespace và tên entity.
