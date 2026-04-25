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

namespace UrbanX.<Service>.Application.Usecases.V1.Command;

// Command là record, implement ICommand<TResult>
// - Nếu trả về entity ID: ICommand<Guid>
// - Nếu không trả về gì: ICommand (không có type param)
public record <Name>Command(
    // ... input parameters
) : ICommand<Guid>;

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

```csharp
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.<Service>.Application.Usecases.V1.Errors;
using UrbanX.<Service>.Domain;

namespace UrbanX.<Service>.Application.Usecases.V1.Command;

public sealed class <Name>CommandHandler : ICommandHandler<<Name>Command, Guid>
{
    private readonly IProductRepository _productRepository;
    // Thêm các dependency cần thiết

    public <Name>CommandHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Result<Guid>> Handle(<Name>Command request, CancellationToken cancellationToken)
    {
        // 1. Fetch entity cần thiết
        var product = await _productRepository.GetByIdForUpdateAsync(request.ProductId, cancellationToken);
        if (product is null)
            return Result.Failure<Guid>(ProductErrors.NotFound(request.ProductId));

        // 2. Business rule checks
        if (await _productRepository.SlugInUseAsync(request.Slug, cancellationToken))
            return Result.Failure<Guid>(ProductErrors.SlugInUse(request.Slug));

        // 3. Thực hiện domain operation
        product.ApplyEdit(/* ... */);

        // 4. Persist (transaction behavior tự wrap SaveChanges)
        await _productRepository.AddAsync(product, cancellationToken);

        // 5. Publish event nếu cần (qua Outbox)
        await _outboxWriter.WriteAsync(integrationEvent, cancellationToken);

        return Result.Success(product.Id);
    }
}
```

**Lưu ý:**
- Handler là `sealed class`, implement `ICommandHandler<TCommand, TResult>`
- Nếu không trả về gì: `ICommandHandler<TCommand>` và `Task<Result>`
- **Không** gọi `SaveChanges()` — `CatalogTransactionBehavior` tự làm
- Trả về `Result.Failure<T>(error)` khi fail, `Result.Success(value)` khi thành công
- Early return ngay khi gặp lỗi, không tiếp tục xử lý

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

    // Dùng static Error (không có param) cho lỗi không cần context
    public static readonly Error Forbidden =
        new("<Entity>.Forbidden", "You do not have permission to perform this action");
}
```

**Lưu ý:**
- Methods với param: `public static Error NotFound(Guid id) => new(...)`
- Constants không cần param: `public static readonly Error Forbidden = new(...)`
- Error code format: `"<Entity>.<ProblemName>"` — PascalCase cả hai phần
- Tham khảo error codes đã có trước khi tạo mới để tránh duplicate

---

### File 4: Carter Endpoint (trong API project)

Thêm vào Carter module hiện có của entity, **không tạo file mới** nếu module đã tồn tại:

**Path:** `src/Services/<Service>/<Service>.API/Apis/<Entity>Apis.cs`

```csharp
// Trong AddRoutes(), thêm route vào group đã có:
group1.MapPut("/{id:guid}", Update<Entity>V1);
group1.MapDelete("/{id:guid}", Delete<Entity>V1);

// Thêm handler method:
public static async Task<IResult> Update<Entity>V1(
    [FromServices] ISender sender,
    [FromRoute] Guid id,
    [FromBody] Update<Entity>Command body,
    CancellationToken cancellationToken)
{
    var result = await sender.Send(body with { Id = id }, cancellationToken);
    if (result.IsFailure)
        return HandleFailure(result);
    return Results.Ok(result.Value);
}

// Nếu tạo resource mới (POST):
return Results.Created($"/api/v1/<service>/<entities>/{result.Value}", result.Value);

// Nếu update/delete thành công không có body:
return Results.NoContent();
```

**HTTP method mapping:**
| Operation | HTTP | Success response |
|---|---|---|
| Create resource | POST | 201 Created + Location header |
| Update resource | PUT / PATCH | 200 OK hoặc 204 NoContent |
| Delete resource | DELETE | 204 NoContent |
| Action (activate, publish...) | POST | 200 OK hoặc 204 NoContent |

---

## Transactional Outbox (khi cần publish event cross-service)

Khi command cần notify service khác (Search, Order, v.v.):

```csharp
// 1. Inject IOutboxWriter vào handler
private readonly IOutboxWriter _outboxWriter;

// 2. Sau khi persist entity, ghi event vào outbox
var integrationEvent = new ProductIntegrationEvents.ProductUpdatedV1(
    product.Id,
    product.Name,
    // ... các field cần thiết
);
await _outboxWriter.WriteAsync(integrationEvent, cancellationToken);
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

namespace UrbanX.Catalog.Application.Usecases.V1.Command;

public record CreateProductCommand(
    string Sku,
    string Name,
    string? Slug,
    string? Description,
    Guid CategoryId,
    Guid? BrandId,
    decimal BasePrice,
    Guid SellerId,
    string SellerName,
    string? Status,
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
    IReadOnlyList<AttributeNameValueItem> Attributes,
    IReadOnlyList<CreateProductImageItem> GalleryImages
);

public record AttributeNameValueItem(string Name, string Value);

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(500);
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.SellerId).NotEmpty();
        RuleFor(x => x.SellerName).NotEmpty().MaximumLength(255);
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

    public CreateProductCommandHandler(
        IProductRepository productRepository,
        ICategoryRepository categoryRepository,
        IBrandRepository brandRepository,
        IAttributeDefinitionRepository attributeDefinitionRepository,
        IOutboxWriter outboxWriter)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _brandRepository = brandRepository;
        _attributeDefinitionRepository = attributeDefinitionRepository;
        _outboxWriter = outboxWriter;
    }

    public async Task<Result<Guid>> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var slug = string.IsNullOrWhiteSpace(request.Slug)
            ? SlugHelper.ToSlug(request.Name)
            : request.Slug!.Trim().ToLowerInvariant();

        if (await _productRepository.SlugInUseAsync(slug, cancellationToken))
            return Result.Failure<Guid>(ProductErrors.SlugInUse(slug));

        if (await _productRepository.SkuInUseAsync(request.Sku, cancellationToken))
            return Result.Failure<Guid>(ProductErrors.SkuInUse(request.Sku));
        foreach (var v in request.Variants)
        {
            if (await _productRepository.SkuInUseAsync(v.Sku, cancellationToken))
                return Result.Failure<Guid>(ProductErrors.SkuInUse(v.Sku));
        }

        var category = await _categoryRepository.GetByIdAsync(request.CategoryId, cancellationToken);
        if (category is null)
            return Result.Failure<Guid>(ProductErrors.CategoryNotFound(request.CategoryId));

        Brand? brand = null;
        if (request.BrandId is { } brandId)
        {
            brand = await _brandRepository.GetByIdAsync(brandId, cancellationToken);
            if (brand is null)
                return Result.Failure<Guid>(ProductErrors.BrandNotFound(brandId));
        }

        var displayOrder = 0;
        var attributeNameById = new Dictionary<Guid, string>();
        var variantSpecs = new List<NewVariantSpec>();

        foreach (var v in request.Variants)
        {
            var valuePairs = new List<(Guid AttributeId, string Value)>();
            foreach (var a in v.Attributes)
            {
                var def = await _attributeDefinitionRepository.GetOrCreateAsync(
                    request.CategoryId, a.Name, AttributeValueTypes.Text,
                    isVariant: true, displayOrder: displayOrder++, cancellationToken);
                attributeNameById[def.Id] = def.Name;
                valuePairs.Add((def.Id, a.Value));
            }

            variantSpecs.Add(new NewVariantSpec(
                v.Sku, v.Name, v.Price, v.CompareAtPrice, v.ImageUrl, v.Barcode,
                valuePairs,
                v.GalleryImages.Select(g => new NewProductImageSpec(g.Url, g.AltText, g.DisplayOrder, g.IsPrimary)).ToList()));
        }

        var product = Product.Create(
            request.Sku, request.Name, slug, request.Description, null,
            request.CategoryId, request.BrandId, category.Name, brand?.Name,
            request.BasePrice, request.SellerId, request.SellerName,
            request.Status ?? ProductStatus.Draft, null, null,
            request.Tags?.ToList() ?? [], null, null,
            request.ProductImages.Select(p => new NewProductImageSpec(p.Url, p.AltText, p.DisplayOrder, p.IsPrimary)).ToList(),
            variantSpecs);

        await _productRepository.AddAsync(product, cancellationToken);
        await _outboxWriter.WriteAsync(MapToCreatedEvent(product, attributeNameById), cancellationToken);

        return Result.Success(product.Id);
    }

    private static ProductIntegrationEvents.ProductCreatedV1 MapToCreatedEvent(
        Product product, IReadOnlyDictionary<Guid, string> attributeNameById) => /* mapping */;
}
```

### API Endpoint

```csharp
// Thêm vào ProductApis.cs — AddRoutes()
group1.MapPost("/", CreateProductV1);

// Handler method
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
| `CatalogTransactionBehavior` | `OrderTransactionBehavior` (hoặc base behavior) |

Pattern và file structure hoàn toàn giống nhau — chỉ khác namespace và tên entity.
