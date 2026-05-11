---
name: add-command
description: Tạo CQRS Command mới (Command record + Validator + Handler + Carter endpoint). Tự động invoke khi user yêu cầu thêm write operation, use case mới, hoặc bất kỳ từ khóa nào như "tạo command", "implement", "thêm feature", "xử lý", "create/update/delete/activate/publish" cho bất kỳ service nào trong UrbanX.
allowed-tools: Read, Grep, LS
---
# Skill: add-command

## Khi nào dùng

Khi người dùng yêu cầu thêm một **write operation** mới vào bất kỳ service nào (Catalog, Order, Payment, v.v.).

**Trigger examples:**
- "thêm command UpdateProduct"
- "implement DeleteVariant"
- "tạo command ActivateProduct"
- `/add-command`

---

## Quy trình

### Bước 1 — Xác định service và feature name

Hỏi (nếu chưa rõ):
- Service nào? (Catalog, Order, Payment, ...)
- Tên command? (PascalCase, ví dụ: `UpdateProduct`, `DeleteVariant`)
- Có dùng Transactional Outbox không? (nếu cần publish event cross-service)

### Bước 2 — Đọc context trước khi viết

**Bắt buộc đọc trước:**
- File errors của service: `src/Services/<Service>/<Service>.Application/Usecases/V1/Errors/`
- Repository interfaces liên quan: `src/Services/<Service>/<Service>.Domain/I*Repository.cs`
- Nếu Outbox: `Shared.Contract/Messaging/<Service>/` để xem event contracts đã có

**Không đọc** các service khác trừ khi được yêu cầu rõ ràng.

### Bước 3 — Tạo các file theo thứ tự

Thứ tự chuẩn: Command → Handler → Errors (nếu cần) → API endpoint

### Bước 4 — Không build, không run

Chỉ viết file. Không chạy `dotnet build` trừ khi người dùng yêu cầu.

---

## Cấu trúc file

### File 1: Command + Validator

**Path:** `src/Services/<Service>/<Service>.Application/Usecases/V1/Command/<Name>/<Name>Command.cs`

```csharp
using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.<Service>.Application.Usecases.V1.Command;

[RequirePermission(Permissions.<Resource>.Write)]  // hoặc [AllowAnonymous]
public record <Name>Command(
    // ... input parameters
) : ICommand<Guid>;  // ICommand nếu không trả về gì

// Nested records cho complex inputs
public record <Name>SomeItem(
    string Field1,
    int? Field2
);

public sealed class <Name>CommandValidator : AbstractValidator<<Name>Command>
{
    public <Name>CommandValidator()
    {
        RuleFor(x => x.SomeField).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Price)
            .GreaterThan(0)
            .LessThanOrEqualTo(1_000_000_000);
        // Nullable fields — dùng .When()
        RuleFor(x => x.OptionalUrl)
            .Must(u => u == null || Uri.TryCreate(u, UriKind.Absolute, out _))
            .When(x => x.OptionalUrl is not null);
        // Collection validation — dùng RuleForEach + ChildRules
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Field1).NotEmpty();
        });
        // Cross-field validation — dùng RuleFor(x => x).Must(...)
        RuleFor(x => x).Must(AllItemsUnique).WithMessage("Items must be unique");
    }

    private static bool AllItemsUnique(<Name>Command c) => /* logic */;
}
```

**Lưu ý:**
- Command là `record`, không phải `class`
- Validator là `sealed class`
- Không inject dependency vào Validator — chỉ validate input shape, không query DB
- Cross-field validation: `RuleFor(x => x).Must(...)`

---

### File 2: Handler

**Path:** `src/Services/<Service>/<Service>.Application/Usecases/V1/Command/<Name>/<Name>CommandHandler.cs`

**Simple handler (ít dependency) — dùng primary constructor:**

```csharp
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.<Service>.Application.Usecases.V1.Errors;
using UrbanX.<Service>.Domain;

namespace UrbanX.<Service>.Application.Usecases.V1.Command;

internal sealed class <Name>CommandHandler(
    I<Entity>Repository repo)
    : ICommandHandler<<Name>Command, Guid>
{
    public async Task<Result<Guid>> Handle(<Name>Command cmd, CancellationToken ct)
    {
        var entity = await repo.GetByIdForUpdateAsync(cmd.EntityId, ct);
        if (entity is null)
            return Result.Failure<Guid>(<Entity>Errors.NotFound(cmd.EntityId));

        entity.SomeOperation();
        await repo.AddAsync(entity, ct);

        return Result.Success(entity.Id);
    }
}
```

**Complex handler (nhiều dependency) — dùng explicit constructor:**

```csharp
public sealed class <Name>CommandHandler : ICommandHandler<<Name>Command, Guid>
{
    private readonly IProductRepository _productRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IUserContext _userContext;

    public <Name>CommandHandler(
        IProductRepository productRepository,
        ICategoryRepository categoryRepository,
        IOutboxWriter outboxWriter,
        IUserContext userContext)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _outboxWriter = outboxWriter;
        _userContext = userContext;
    }

    public async Task<Result<Guid>> Handle(<Name>Command cmd, CancellationToken ct)
    {
        // 1. Fetch entity
        var product = await _productRepository.GetByIdForUpdateAsync(cmd.ProductId, ct);
        if (product is null)
            return Result.Failure<Guid>(ProductErrors.NotFound(cmd.ProductId));

        // 2. Business rule checks
        if (await _productRepository.SlugInUseAsync(cmd.Slug, ct))
            return Result.Failure<Guid>(ProductErrors.SlugInUse(cmd.Slug));

        // 3. Domain operation
        product.ApplyEdit(/* ... */);

        // 4. Persist (TransactionPipelineBehavior tự wrap SaveChanges)
        await _productRepository.AddAsync(product, ct);

        // 5. Publish event qua Outbox
        await _outboxWriter.WriteAsync(new SomeEventV1(product.Id, ...), ct);

        return Result.Success(product.Id);
    }
}
```

**Lưu ý:**
- Handler là `internal sealed class` (hoặc `public sealed class` — nhất quán với codebase hiện tại), implement `ICommandHandler<TCommand, TResult>`
- Nếu không trả về gì: `ICommandHandler<TCommand>` và `Task<Result>`
- **Không** gọi `SaveChanges()` — `TransactionPipelineBehavior` tự làm
- Trả về `Result.Failure<T>(error)` khi fail, `Result.Success(value)` khi thành công
- Early return ngay khi gặp lỗi, không tiếp tục xử lý
- `IUserContext` chỉ inject khi cần `UserId` cho ownership check hoặc audit

---

### File 3: Errors (nếu cần thêm error code mới)

**Path:** `src/Services/<Service>/<Service>.Application/Usecases/V1/Errors/<Entity>Errors.cs`

```csharp
using Shared.Kernel.Primitives;

namespace UrbanX.<Service>.Application.Usecases.V1.Errors;

public static class <Entity>Errors
{
    public static Error NotFound(Guid id) =>
        new("<Entity>.NotFound", $"<Entity> {id} not found");

    public static Error SlugInUse(string slug) =>
        new("<Entity>.SlugInUse", $"The slug \"{slug}\" is already in use");

    public static readonly Error Forbidden =
        new("<Entity>.Forbidden", "You do not have permission to perform this action");
}
```

**Lưu ý:**
- Methods với param: `public static Error NotFound(Guid id) => new(...)`
- Constants không cần param: `public static readonly Error AlreadyExists = new(...)`
- Error code format: `"<Entity>.<ProblemName>"` — PascalCase cả hai phần
- Tham khảo error codes đã có trước khi tạo mới để tránh duplicate

---

### File 4: Carter Endpoint (trong API project)

Thêm vào Carter module hiện có của entity, **không tạo file mới** nếu module đã tồn tại:

**Path:** `src/Services/<Service>/<Service>.API/Apis/<Entity>Apis.cs`

```csharp
// Trong AddRoutes(), thêm route vào group đã có:
group1.MapPost("/", Create<Entity>V1);
group1.MapPatch("/{id:guid}", Update<Entity>V1);
group1.MapDelete("/{id:guid}", Delete<Entity>V1);
```

**POST (create) — trả 201 Created:**
```csharp
public static async Task<IResult> Create<Entity>V1(
    [FromServices] ISender sender,
    [FromBody] Create<Entity>Command body,
    CancellationToken cancellationToken)
{
    var result = await sender.Send(body, cancellationToken);
    if (result.IsFailure)
        return HandleFailure(result);
    return Results.Created($"/api/v1/<service>/<entities>/{result.Value}", result.Value);
}
```

**PATCH / PUT (update) — trả 204 NoContent:**
```csharp
public static async Task<IResult> Update<Entity>V1(
    Guid id,
    [FromServices] ISender sender,
    [FromBody] Update<Entity>Command body,
    CancellationToken cancellationToken)
{
    var result = await sender.Send(body with { Id = id }, cancellationToken);
    return result.IsFailure ? ToCatalogResult(result) : Results.NoContent();
}
```

**DELETE / Action (activate, publish...) — trả 204 NoContent:**
```csharp
public static async Task<IResult> Delete<Entity>V1(
    Guid id,
    [FromServices] ISender sender,
    CancellationToken cancellationToken)
{
    var result = await sender.Send(new Delete<Entity>Command(id), cancellationToken);
    return result.IsFailure ? ToCatalogResult(result) : Results.NoContent();
}
```

**HTTP method mapping:**
| Operation | HTTP | Success response | Error helper |
|---|---|---|---|
| Create resource | POST | 201 Created + Location | `HandleFailure(result)` |
| Update resource | PUT / PATCH | 204 NoContent | `ToCatalogResult(result)` |
| Delete resource | DELETE | 204 NoContent | `ToCatalogResult(result)` |
| Action (activate...) | POST | 200 OK hoặc 204 | `ToCatalogResult(result)` |

---

## Transactional Outbox (khi cần publish event cross-service)

```csharp
// 1. Inject IOutboxWriter vào handler
// 2. Sau khi persist entity, ghi event vào outbox
await _outboxWriter.WriteAsync(new ProductIntegrationEvents.ProductUpdatedV1(
    product.Id,
    product.Name,
    // ... các field cần thiết
), ct);
```

**Event contracts** ở `Shared.Contract/Messaging/<Service>/`:
- Xem các event đã có trước khi tạo mới
- Event record kế thừa `IntegrationEventBase`
- Namespace: `Shared.Contract.Messaging.<Service>`

---

## Outbox vs Direct Publish

| Khi nào | Dùng gì |
|---|---|
| Event phải đảm bảo delivery (cross-service, critical) | `IOutboxWriter` |
| Event in-process (domain event, same transaction) | `IDomainEventPublisher` |
| Không cần publish event | Không cần gì |

---

## Checklist trước khi xong

- [ ] Command là `record`, implement đúng `ICommand<T>`
- [ ] Authorization attribute đúng: `[RequirePermission]` hoặc `[AllowAnonymous]`
- [ ] Validator là `sealed class`, không query DB
- [ ] Handler là `sealed class`, early return khi fail
- [ ] Không gọi `SaveChanges()` trong handler
- [ ] Mỗi error path → `Result.Failure<T>(specificError)`
- [ ] Nếu dùng Outbox: `WriteAsync` sau `AddAsync`, trước `return`
- [ ] API endpoint thêm vào Carter module đã có (không tạo file mới)
- [ ] Error codes không duplicate với codes đã có
- [ ] Doc: tạo/cập nhật `docs/<service>/<feature>.md`

---

## Ví dụ đầy đủ — CreateProduct (Catalog)

### Command + Validator

```csharp
// src/Services/Catalog/UrbanX.Catalog.Application/Usecases/V1/Command/CreateProduct/CreateProductCommand.cs
using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Catalog.Application.Usecases.V1.Command;

[RequirePermission(Permissions.Products.Write)]
public record CreateProductCommand(
    string Sku,
    string Name,
    string? Slug,
    string? Description,
    string? ShortDescription,
    Guid CategoryId,
    Guid? BrandId,
    decimal BasePrice,
    string? Status,
    int? WeightGrams,
    ProductDimensionsInput? Dimensions,
    List<string>? Tags,
    string? MetaTitle,
    string? MetaDescription,
    IReadOnlyList<CreateProductImageItem> ProductImages,
    IReadOnlyList<CreateProductVariantItem> Variants
) : ICommand<Guid>;

public record CreateProductImageItem(string Url, string? AltText, int DisplayOrder, bool IsPrimary);

public record CreateProductVariantItem(
    string Sku,
    string? Name,
    decimal Price,
    decimal? CompareAtPrice,
    string? ImageUrl,
    string? Barcode,
    IReadOnlyList<AttributeNameValueItem> Attributes,
    IReadOnlyList<CreateProductImageItem> GalleryImages
);

public record AttributeNameValueItem(string Name, string Value);
public record ProductDimensionsInput(decimal? LengthCm, decimal? WidthCm, decimal? HeightCm);

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(500);
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.BasePrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Variants).NotNull().NotEmpty();
        RuleFor(x => x.ProductImages).NotNull();
        RuleForEach(x => x.ProductImages).ChildRules(p =>
            p.RuleFor(i => i.Url)
             .Must(u => Uri.TryCreate(u, UriKind.Absolute, out _))
             .WithMessage("Invalid product image URL"));
        RuleFor(x => x).Must(AllSkusUniqueInRequest)
            .WithMessage("Product and variant SKUs must be unique within the request");
        RuleForEach(x => x.Variants).SetValidator(new CreateProductVariantItemValidator());
    }

    private static bool AllSkusUniqueInRequest(CreateProductCommand c)
    {
        var all = c.Variants.Select(v => v.Sku).Prepend(c.Sku);
        return all.Count() == all.Distinct(StringComparer.OrdinalIgnoreCase).Count();
    }
}

public sealed class CreateProductVariantItemValidator : AbstractValidator<CreateProductVariantItem>
{
    public CreateProductVariantItemValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Price).GreaterThan(0).LessThanOrEqualTo(1_000_000_000);
        RuleFor(x => x.CompareAtPrice).GreaterThan(0).When(x => x.CompareAtPrice is not null);
        RuleFor(x => x.ImageUrl)
            .Must(u => u == null || Uri.TryCreate(u, UriKind.Absolute, out _))
            .WithMessage("Invalid variant image URL");
        RuleFor(x => x.Attributes).NotNull();
        RuleFor(x => x.GalleryImages).NotNull();
    }
}
```

### Handler

```csharp
// src/Services/Catalog/UrbanX.Catalog.Application/Usecases/V1/Command/CreateProduct/CreateProductCommandHandler.cs
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Contract.Messaging.Catalog;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;
using UrbanX.Catalog.Application.Usecases.V1.Errors;
using UrbanX.Catalog.Domain;
using UrbanX.Catalog.Domain.Helpers;
using UrbanX.Catalog.Domain.Models;
using UrbanX.Catalog.Domain.ValueObjects;

namespace UrbanX.Catalog.Application.Usecases.V1.Command;

public sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Guid>
{
    private readonly IProductRepository _productRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IBrandRepository _brandRepository;
    private readonly IAttributeDefinitionRepository _attributeDefinitionRepository;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IUserContext _userContext;

    public CreateProductCommandHandler(
        IProductRepository productRepository,
        ICategoryRepository categoryRepository,
        IBrandRepository brandRepository,
        IAttributeDefinitionRepository attributeDefinitionRepository,
        IOutboxWriter outboxWriter,
        IUserContext userContext)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _brandRepository = brandRepository;
        _attributeDefinitionRepository = attributeDefinitionRepository;
        _outboxWriter = outboxWriter;
        _userContext = userContext;
    }

    public async Task<Result<Guid>> Handle(CreateProductCommand cmd, CancellationToken ct)
    {
        var slug = string.IsNullOrWhiteSpace(cmd.Slug)
            ? SlugHelper.ToSlug(cmd.Name)
            : cmd.Slug!.Trim().ToLowerInvariant();

        if (await _productRepository.SlugInUseAsync(slug, ct))
            return Result.Failure<Guid>(ProductErrors.SlugInUse(slug));

        if (await _productRepository.SkuInUseAsync(cmd.Sku, ct))
            return Result.Failure<Guid>(ProductErrors.SkuInUse(cmd.Sku));

        var category = await _categoryRepository.GetByIdAsync(cmd.CategoryId, ct);
        if (category is null)
            return Result.Failure<Guid>(ProductErrors.CategoryNotFound(cmd.CategoryId));

        // ... build product, persist, write outbox event

        await _productRepository.AddAsync(product, ct);
        await _outboxWriter.WriteAsync(MapToCreatedEvent(product), ct);

        return Result.Success(product.Id);
    }
}
```

### API Endpoint

```csharp
// Thêm vào ProductApis.cs — AddRoutes()
group1.MapPost("/product", CreateProductV1);

public static async Task<IResult> CreateProductV1(
    [FromServices] ISender sender,
    [FromBody] CreateProductCommand body,
    CancellationToken cancellationToken)
{
    var result = await sender.Send(body, cancellationToken);
    if (result.IsFailure)
        return HandleFailure(result);
    return Results.Created($"/api/v1/catalog/products/{result.Value}", result.Value);
}
```

---

## Adapter cho service khác

Khi implement command cho **service khác ngoài Catalog**, thay namespace và project prefix:

| Catalog | Order / Payment / ... |
|---|---|
| `UrbanX.Catalog.Application` | `UrbanX.Order.Application` |
| `UrbanX.Catalog.Domain` | `UrbanX.Order.Domain` |
| `IProductRepository` | `IOrderRepository` (tương đương) |

Pattern và file structure hoàn toàn giống nhau — chỉ khác namespace và tên entity.
