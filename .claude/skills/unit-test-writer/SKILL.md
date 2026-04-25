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
    // Arrange — setup dependencies và input data
    var categoryId = Guid.NewGuid();
    _categoryRepository
        .Setup(r => r.GetByIdAsync(categoryId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Category { Id = categoryId, Name = "Electronics" });
    var command = ValidCommand() with { CategoryId = categoryId };

    // Act — thực thi handler
    var result = await _handler.Handle(command, CancellationToken.None);

    // Assert — kiểm tra kết quả
    Assert.True(result.IsSuccess);
    Assert.NotEqual(Guid.Empty, result.Value);
}
```

**Lưu ý:**
- Luôn viết comment `// Arrange`, `// Act`, `// Assert` để rõ ràng structure
- Mỗi test chỉ test MỘT thứ duy nhất (single responsibility)
- Setup logic phức tạp nên đưa vào `Arrange` section, không vào helper method

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

// Nếu cần mock với specific ID, setup cụ thể rồi lambda trả về entity hợp lý
_categoryRepository
    .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync((Guid id) => new Category { Id = id, Name = "Electronics" });
```

**Generic method mocking (IOutboxWriter):**

`IOutboxWriter.WriteAsync<TEvent>()` là generic method. Moq match theo **type parameter**, không phải interface base. Phải dùng **concrete event type**:

```csharp
// ❌ Sai — không match khi handler gọi WriteAsync<ProductCreatedV1>()
_outboxWriter
    .Setup(w => w.WriteAsync(It.IsAny<IIntegrationEvent>(), It.IsAny<CancellationToken>()))
    .Returns(Task.CompletedTask);

// ✅ Đúng
_outboxWriter
    .Setup(w => w.WriteAsync(It.IsAny<ProductIntegrationEvents.ProductCreatedV1>(), It.IsAny<CancellationToken>()))
    .Returns(Task.CompletedTask);
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

Mỗi `Result.Failure<T>` trong handler → một `[Fact]` riêng. Ngoài ra, thêm test kiểm tra side effect (repo calls, outbox writes) để đảm bảo handler **KHÔNG** làm việc không cần thiết khi fail:

```csharp
[Fact]
public async Task Handle_WhenSlugInUse_ReturnsSlugInUseError()
{
    // Arrange
    SetupHappyPath();
    _productRepository
        .Setup(r => r.SlugInUseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(true);

    // Act
    var result = await _handler.Handle(ValidCommand(), CancellationToken.None);

    // Assert — failure
    Assert.True(result.IsFailure);
    Assert.Equal(ProductErrors.SlugInUse("x").Code, result.Error.Code);
}

[Fact]
public async Task Handle_WhenSlugInUse_DoesNotCallAddAsync()
{
    // Arrange
    SetupHappyPath();
    _productRepository
        .Setup(r => r.SlugInUseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(true);

    // Act
    await _handler.Handle(ValidCommand(), CancellationToken.None);

    // Assert — không lãng phí resources
    _productRepository.Verify(
        r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()),
        Times.Never);
    _outboxWriter.Verify(
        w => w.WriteAsync(It.IsAny<ProductIntegrationEvents.ProductCreatedV1>(), It.IsAny<CancellationToken>()),
        Times.Never);
}
```

**Dùng [Theory] + [InlineData] tránh duplication:**

```csharp
[Theory]
[InlineData("SKU-001")]  // Product SKU
[InlineData("VAR-001")]  // Variant SKU
public async Task Handle_WhenSkuInUse_ReturnsSkuInUseError(string inUseSku)
{
    // Arrange
    SetupHappyPath();
    _productRepository
        .Setup(r => r.SkuInUseAsync(inUseSku, It.IsAny<CancellationToken>()))
        .ReturnsAsync(true);
    var command = inUseSku == "SKU-001"
        ? ValidCommand()
        : ValidCommand() with { Variants = [ValidVariant() with { Sku = inUseSku }] };

    // Act
    var result = await _handler.Handle(command, CancellationToken.None);

    // Assert
    Assert.True(result.IsFailure);
    Assert.Equal(ProductErrors.SkuInUse(inUseSku).Code, result.Error.Code);
}
```

**Edge cases & behavior verification:**

```csharp
[Fact]
public async Task Handle_WithMultipleVariants_ReturnsSuccessAndCallsAddOnce()
{
    // Arrange
    SetupHappyPath();
    var command = ValidCommand() with
    {
        Variants =
        [
            ValidVariant() with { Sku = "VAR-001" },
            ValidVariant() with { Sku = "VAR-002" }
        ]
    };

    // Act
    var result = await _handler.Handle(command, CancellationToken.None);

    // Assert
    Assert.True(result.IsSuccess);
    _productRepository.Verify(
        r => r.AddAsync(It.Is<Product>(p => p.Variants.Count == 2), It.IsAny<CancellationToken>()),
        Times.Once);
}
```

### Validator Tests

Dùng FluentValidation `TestValidate`. Bao gồm cả **positive cases** (hợp lệ) lẫn **negative cases** (không hợp lệ):

```csharp
using FluentValidation.TestHelper;

public class CreateProductCommandValidatorTests
{
    private readonly CreateProductCommandValidator _validator = new();

    [Fact]
    public void Validate_WithValidCommand_HasNoErrors()
    {
        // Happy path — không nên bỏ
        var result = _validator.TestValidate(ValidCommand());
        result.ShouldNotHaveAnyValidationErrors();
    }

    // Tránh duplication: dùng [Theory] + [InlineData]
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenSkuIsEmpty_HasValidationError(string sku)
    {
        var command = ValidCommand() with { Sku = sku };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Sku);
    }

    // Positive case — đảm bảo case hợp lệ thực sự pass
    [Theory]
    [InlineData(1)]
    [InlineData(100_000)]
    [InlineData(1_000_000_000)]
    public void Validate_WhenPriceIsValid_HasNoError(decimal price)
    {
        var command = ValidCommand() with { BasePrice = price };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.BasePrice);
    }

    private static CreateProductCommand ValidCommand() => new(
        Sku: "SKU-001",
        Name: "Test Product",
        // ... các field bắt buộc
    );
}
```

**Với child rules (RuleForEach, nested validation):**

```csharp
[Fact]
public void Validate_WhenProductImageHasInvalidUrl_HasValidationError()
{
    var command = ValidCommand() with
    {
        ProductImages = [new CreateProductImageItem("not-a-valid-url", null, 0, true)]
    };
    var result = _validator.TestValidate(command);
    
    // Child rule error — không dùng ShouldHaveValidationErrorFor (không match)
    Assert.NotEmpty(result.Errors);
    Assert.True(result.Errors.Any(e => e.PropertyName.Contains("Url")));
}

[Fact]
public void Validate_WhenProductImageUrlIsValid_HasNoError()
{
    var command = ValidCommand() with
    {
        ProductImages = 
        [
            new CreateProductImageItem("https://example.com/img.jpg", "alt", 0, true)
        ]
    };
    var result = _validator.TestValidate(command);
    
    // Positive case — kiểm tra không có lỗi trên ProductImages
    Assert.False(result.Errors.Any(e => e.PropertyName.Contains("ProductImages")));
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

## Common Mistakes to Avoid

**❌ Weak Assertions:**
```csharp
var result = _validator.TestValidate(command);
Assert.NotEmpty(result.Errors);  // Không biết error ở field nào, rule nào
```

**✅ Specific Assertions:**
```csharp
var result = _validator.TestValidate(command);
result.ShouldHaveValidationErrorFor(x => x.Sku);  // Rõ ràng field bị lỗi

// Với child rules
Assert.True(result.Errors.Any(e => e.PropertyName.Contains("Url")));  // Check property name
```

**❌ Không verify side effects:**
```csharp
var result = await _handler.Handle(command, CancellationToken.None);
Assert.True(result.IsSuccess);  // Chỉ check result, không check nó có call repo/outbox không?
```

**✅ Verify side effects:**
```csharp
var result = await _handler.Handle(command, CancellationToken.None);
Assert.True(result.IsSuccess);
_productRepository.Verify(r => r.AddAsync(...), Times.Once);      // Repository được gọi
_outboxWriter.Verify(w => w.WriteAsync(...), Times.Once);         // Outbox được gọi
```

**❌ Không test failure → side effect:**
```csharp
// Handler fail khi slug in use, nhưng vẫn call repository?
var result = await _handler.Handle(command, CancellationToken.None);
Assert.True(result.IsFailure);  // Chỉ check failure, quên verify repo KHÔNG được call
```

**✅ Verify failure không lãng phí resources:**
```csharp
var result = await _handler.Handle(command, CancellationToken.None);
Assert.True(result.IsFailure);
_productRepository.Verify(r => r.AddAsync(...), Times.Never);      // KHÔNG call
_outboxWriter.Verify(w => w.WriteAsync(...), Times.Never);         // KHÔNG call
```

- [ ] **Cấu trúc:** Mỗi test có comment `// Arrange`, `// Act`, `// Assert` rõ ràng
- [ ] **Happy path:** Có ít nhất 1 test cho success case
- [ ] **Failure paths:** Mỗi `Result.Failure<T>` → 1 test riêng
- [ ] **Side effects:** Verify repository/outbox methods được gọi (hoặc KHÔNG gọi khi nên)
- [ ] **Positive + Negative:** Validator có cả case hợp lệ và không hợp lệ
- [ ] **Tránh duplication:** Dùng `[Theory] + [InlineData]` cho test tương tự
- [ ] **Không magic strings:** Dùng `ProductErrors.Xxx.Code` thay vì hardcode code
- [ ] **Mock setup đúng:** `It.IsAny<CancellationToken>()` thay vì `default`
- [ ] **Helper methods:** `ValidCommand()`, `SetupHappyPath()` rõ ràng, tái sử dụng được
- [ ] **Edge cases:** Test multiple items, null values, boundary values (max/min)
- [ ] **Weak assertions:** Thay `Assert.NotEmpty()` → `Assert.True(result.Errors.Any(e => e.PropertyName.Contains(“Field”)))`
- [ ] **Child rules:** Dùng `Assert.NotEmpty(result.Errors)` + property check, không dùng `ShouldHaveValidationErrorFor`
---

## Ví dụ đầy đủ — CreateProductCommandHandlerTests (improved)

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
        // Arrange
        SetupHappyPath();
        var command = ValidCommand();

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
    }

    [Fact]
    public async Task Handle_WithValidCommand_WritesOutboxOnce()
    {
        // Arrange
        SetupHappyPath();
        var command = ValidCommand();

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _outboxWriter.Verify(
            w => w.WriteAsync(It.IsAny<ProductIntegrationEvents.ProductCreatedV1>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenSlugInUse_ReturnsSlugInUseError()
    {
        // Arrange
        SetupHappyPath();
        _productRepository
            .Setup(r => r.SlugInUseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(ValidCommand(), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.SlugInUse("x").Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenSlugInUse_DoesNotCallAddAsync()
    {
        // Arrange
        SetupHappyPath();
        _productRepository
            .Setup(r => r.SlugInUseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _handler.Handle(ValidCommand(), CancellationToken.None);

        // Assert — verify không lãng phí resources
        _productRepository.Verify(
            r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _outboxWriter.Verify(
            w => w.WriteAsync(It.IsAny<ProductIntegrationEvents.ProductCreatedV1>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("SKU-001")]
    [InlineData("VAR-001")]
    public async Task Handle_WhenSkuInUse_ReturnsSkuInUseError(string inUseSku)
    {
        // Arrange
        SetupHappyPath();
        _productRepository
            .Setup(r => r.SkuInUseAsync(inUseSku, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var command = inUseSku == "SKU-001"
            ? ValidCommand()
            : ValidCommand() with { Variants = [ValidVariant() with { Sku = inUseSku }] };

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.SkuInUse(inUseSku).Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenCategoryNotFound_ReturnsCategoryNotFoundError()
    {
        // Arrange
        SetupHappyPath();
        var categoryId = Guid.NewGuid();
        _categoryRepository
            .Setup(r => r.GetByIdAsync(categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);
        var command = ValidCommand() with { CategoryId = categoryId };

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.CategoryNotFound(categoryId).Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_WithMultipleVariants_ReturnsSuccessAndCallsAddWithCorrectCount()
    {
        // Arrange
        SetupHappyPath();
        var command = ValidCommand() with
        {
            Variants =
            [
                ValidVariant() with { Sku = "VAR-001" },
                ValidVariant() with { Sku = "VAR-002" }
            ]
        };

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _productRepository.Verify(
            r => r.AddAsync(It.Is<Product>(p => p.Variants.Count == 2), It.IsAny<CancellationToken>()),
            Times.Once);
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
            .ReturnsAsync((Guid id) => new Category { Id = id, Name = "Electronics" });
        _outboxWriter
            .Setup(w => w.WriteAsync(It.IsAny<ProductIntegrationEvents.ProductCreatedV1>(), It.IsAny<CancellationToken>()))
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
        Variants: [ValidVariant()]);

    private static CreateProductVariantItem ValidVariant() => new(
        Sku: "VAR-001",
        Name: "Default",
        Price: 100_000,
        CompareAtPrice: null,
        ImageUrl: null,
        Barcode: null,
        Attributes: [],
        GalleryImages: []);
}
```
