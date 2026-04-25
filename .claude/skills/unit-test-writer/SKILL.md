# Skill: unit-test-writer

## Khi nào dùng

Khi người dùng yêu cầu viết unit test cho một handler, validator, domain entity, hoặc bất kỳ class nào trong project UrbanX.

**Trigger examples:**
- "viết unit test cho CreateProductCommandHandler"
- "thêm test cho validator này"
- "cover handler này bằng unit test"
- `/unit-test-writer`

---

## Quy trình

### Bước 1 — Đọc code cần test

Đọc file source trước khi viết bất kỳ test nào:
- Handler: `src/Services/<Service>/<Service>.Application/Usecases/V1/Command/<Name>/<Name>CommandHandler.cs`
- Validator: cùng file với Command (thường đặt chung trong `<Name>Command.cs`)
- Domain entity: `src/Services/<Service>/<Service>.Domain/Models/`
- Errors: `src/Services/<Service>/<Service>.Application/Usecases/V1/Errors/`

### Bước 2 — Xác định test file location

Test project: `tests/UrbanX.Services.Catalog.UnitTests/`

Ánh xạ source → test:

| Source | Test |
|---|---|
| `Usecases/V1/Command/CreateProduct/CreateProductCommandHandler.cs` | `Usecases/V1/Command/CreateProduct/CreateProductCommandHandlerTests.cs` |
| `Usecases/V1/Command/CreateProduct/CreateProductCommand.cs` (validator) | `Usecases/V1/Command/CreateProduct/CreateProductCommandValidatorTests.cs` |
| `Domain/Models/Product.cs` | `Domain/ProductTests.cs` |

### Bước 3 — Viết test

Tuân theo conventions bên dưới.

### Bước 4 — Không chạy hay build

Chỉ viết file. Không cần chạy `dotnet test` trừ khi người dùng yêu cầu.

---

## Test Framework & Packages

| Package | Version | Mục đích |
|---|---|---|
| xUnit | 2.9.3 | Test framework |
| Moq | 4.20.72 | Mocking |
| Microsoft.EntityFrameworkCore.InMemory | 10.0.x | DB in-memory (dùng cho integration, tránh dùng trong unit test) |

**Không có FluentAssertions.** Dùng `Assert` của xUnit.

---

## Conventions

### Naming

- **Test class:** `{SubjectUnderTest}Tests` — ví dụ: `CreateProductCommandHandlerTests`
- **Test method:** `{MethodName}_{Scenario}_{ExpectedResult}` — ví dụ:
  - `Handle_WhenSlugInUse_ReturnsFailure`
  - `Handle_WithValidCommand_ReturnsProductId`
  - `Validate_WhenSkuIsEmpty_HasValidationError`

### Cấu trúc mỗi test (AAA)

```csharp
[Fact]
public async Task Handle_WithValidCommand_ReturnsProductId()
{
    // Arrange
    ...

    // Act
    var result = await _handler.Handle(command, CancellationToken.None);

    // Assert
    Assert.True(result.IsSuccess);
    Assert.NotEqual(Guid.Empty, result.Value);
}
```

### Class structure

```csharp
namespace UrbanX.Services.Catalog.UnitTests.Usecases.V1.Command.CreateProduct;

public class CreateProductCommandHandlerTests
{
    private readonly Mock<IProductRepository> _productRepository = new();
    // ... các mock khác

    private readonly CreateProductCommandHandler _handler;

    public CreateProductCommandHandlerTests()
    {
        _handler = new CreateProductCommandHandler(
            _productRepository.Object,
            // ...
        );
    }

    // tests...
}
```

---

## Patterns theo loại

### Command Handler Tests

**Mocking repositories:**

```csharp
_productRepository
    .Setup(r => r.SlugInUseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(false);

_categoryRepository
    .Setup(r => r.GetByIdAsync(command.CategoryId, It.IsAny<CancellationToken>()))
    .ReturnsAsync(new Category { Id = command.CategoryId, Name = "Electronics" });
```

**Kiểm tra Result:**

```csharp
// Success
Assert.True(result.IsSuccess);
Assert.Equal(expectedId, result.Value);

// Failure
Assert.True(result.IsFailure);
Assert.Equal("Product.SlugInUse", result.Error.Code);
```

**Verify outbox được ghi:**

```csharp
// IOutboxWriter.WriteAsync là generic method — Moq match theo type parameter.
// Phải dùng concrete event type, không dùng IIntegrationEvent (sẽ không match).
_outboxWriter.Verify(
    w => w.WriteAsync(It.IsAny<ProductIntegrationEvents.ProductCreatedV1>(), It.IsAny<CancellationToken>()),
    Times.Once);
```

**Verify repository Add được gọi:**

```csharp
_productRepository.Verify(
    r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()),
    Times.Once);
```

**Test các failure paths:**

Mỗi `Result.Failure<T>` trong handler → một `[Fact]` riêng:
- Slug đã tồn tại → `Handle_WhenSlugInUse_ReturnsSlugInUseError`
- SKU đã tồn tại → `Handle_WhenSkuInUse_ReturnsSkuInUseError`
- Category không tồn tại → `Handle_WhenCategoryNotFound_ReturnsCategoryNotFoundError`
- Brand không tồn tại → `Handle_WhenBrandIdProvidedButNotFound_ReturnsBrandNotFoundError`

### Validator Tests

Dùng FluentValidation `TestValidate`:

```csharp
using FluentValidation.TestHelper;

public class CreateProductCommandValidatorTests
{
    private readonly CreateProductCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenSkuIsEmpty_HasValidationError()
    {
        var command = ValidCommand() with { Sku = "" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Sku);
    }

    [Fact]
    public void Validate_WithValidCommand_HasNoErrors()
    {
        var result = _validator.TestValidate(ValidCommand());
        result.ShouldNotHaveAnyValidationErrors();
    }

    private static CreateProductCommand ValidCommand() => new(
        Sku: "SKU-001",
        Name: "Test Product",
        // ... các field bắt buộc
    );
}
```

**Lưu ý:**
- `FluentValidation.TestHelper` đã có sẵn trong `FluentValidation` >= 11 — không cần package riêng.
- Thêm `<PackageReference Include="FluentValidation" />` vào test `.csproj` nếu chưa có.
- `ShouldHaveAnyValidationError()` **không tồn tại** trong FluentValidation 12.x. Với child rules (ví dụ: `RuleForEach`), dùng `Assert.NotEmpty(result.Errors)` thay thế.

### Domain Entity Tests

```csharp
public class ProductTests
{
    [Fact]
    public void Create_WithValidInputs_SetsPropertiesCorrectly()
    {
        var product = Product.Create("SKU-001", "Test", ...);

        Assert.Equal("SKU-001", product.Sku);
        Assert.Equal("Test", product.Name);
    }

    [Fact]
    public void Create_WhenVariantsEmpty_ThrowsDomainException()
    {
        Assert.Throws<ProductExceptions.VariantsAreRequired>(
            () => Product.Create(..., variants: []));
    }
}
```

---

## Checklist trước khi giao

- [ ] Mỗi failure path trong handler có ít nhất 1 test
- [ ] Happy path có test
- [ ] Validator test có case hợp lệ và ít nhất 1 case invalid cho mỗi rule quan trọng
- [ ] Không dùng magic strings — dùng constant hoặc lấy từ `ProductErrors.Xxx.Code`
- [ ] Mock setup với `It.IsAny<CancellationToken>()` thay vì `default`
- [ ] Test class có `private` helper method `ValidCommand()` hoặc `BuildCommand()` để tái sử dụng
- [ ] Tránh một số test assert quá “weak”. Ví dụ: thay vì Assert.NotEmpty(result.Errors); -> không biết lỗi ở field nào, không đảm bảo đúng rule bị lỗi -> Thay vào đó, assert cụ thể hơn: result.ShouldHaveValidationErrorFor(x => x.ProductImages); hoặc result.Errors.Should().Contain(e => e.PropertyName == "ProductImages[0].Url");
- [ ] Duplicate pattern → nên dùng [Theory] + [InlineData] hoặc [MemberData] để tránh code duplication trong test method.
---

## Ví dụ đầy đủ — CreateProductCommandHandlerTests

```csharp
namespace UrbanX.Services.Catalog.UnitTests.Usecases.V1.Command.CreateProduct;

public class CreateProductCommandHandlerTests
{
    private readonly Mock<IProductRepository> _productRepository = new();
    private readonly Mock<ICategoryRepository> _categoryRepository = new();
    private readonly Mock<IBrandRepository> _brandRepository = new();
    private readonly Mock<IAttributeDefinitionRepository> _attributeDefinitionRepository = new();
    private readonly Mock<IOutboxWriter> _outboxWriter = new();

    private readonly CreateProductCommandHandler _handler;

    public CreateProductCommandHandlerTests()
    {
        _handler = new CreateProductCommandHandler(
            _productRepository.Object,
            _categoryRepository.Object,
            _brandRepository.Object,
            _attributeDefinitionRepository.Object,
            _outboxWriter.Object);
    }

    [Fact]
    public async Task Handle_WithValidCommand_ReturnsSuccessWithProductId()
    {
        SetupHappyPath();
        var command = ValidCommand();

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
        _outboxWriter.Verify(
            w => w.WriteAsync(It.IsAny<IIntegrationEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenSlugInUse_ReturnsFailure()
    {
        SetupHappyPath();
        _productRepository
            .Setup(r => r.SlugInUseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.SlugInUse("test-product").Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenCategoryNotFound_ReturnsFailure()
    {
        SetupHappyPath();
        _categoryRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        var result = await _handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Product.CategoryNotFound", result.Error.Code);
    }

    private void SetupHappyPath()
    {
        _productRepository
            .Setup(r => r.SlugInUseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _productRepository
            .Setup(r => r.SkuInUseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _productRepository
            .Setup(r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _categoryRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Category { Id = Guid.NewGuid(), Name = "Electronics" });
        _outboxWriter
            .Setup(w => w.WriteAsync(It.IsAny<IIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static CreateProductCommand ValidCommand() => new(
        Sku: "SKU-001",
        Name: "Test Product",
        Slug: null,
        Description: "Description",
        ShortDescription: null,
        CategoryId: Guid.NewGuid(),
        BrandId: null,
        BasePrice: 100_000,
        SellerId: Guid.NewGuid(),
        SellerName: "Test Seller",
        Status: ProductStatus.Draft,
        WeightGrams: null,
        Dimensions: null,
        Tags: null,
        MetaTitle: null,
        MetaDescription: null,
        ProductImages: [],
        Variants:
        [
            new CreateProductVariantItem(
                Sku: "VAR-001",
                Name: "Default",
                Price: 100_000,
                CompareAtPrice: null,
                ImageUrl: null,
                Barcode: null,
                Attributes: [],
                GalleryImages: [])
        ]);
}
```
