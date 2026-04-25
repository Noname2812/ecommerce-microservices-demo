using Moq;
using Shared.Application.Authorization;
using Shared.Contract.Messaging.Catalog;
using Shared.Outbox.Abstractions;
using UrbanX.Catalog.Application.Usecases.V1.Command;
using UrbanX.Catalog.Application.Usecases.V1.Errors;
using UrbanX.Catalog.Domain;
using UrbanX.Catalog.Domain.Models;
using UrbanX.Catalog.Domain.ValueObjects;

namespace UrbanX.Services.Catalog.UnitTests.Usecases.V1.Command.CreateProduct;

public class CreateProductCommandHandlerTests
{
    private readonly Mock<IProductRepository> _productRepository = new();
    private readonly Mock<ICategoryRepository> _categoryRepository = new();
    private readonly Mock<IBrandRepository> _brandRepository = new();
    private readonly Mock<IAttributeDefinitionRepository> _attributeDefinitionRepository = new();
    private readonly Mock<IOutboxWriter> _outboxWriter = new();
    private readonly Mock<IUserContext> _userContext = new();
    private readonly Guid _testUserId = Guid.NewGuid();

    private readonly CreateProductCommandHandler _handler;

    public CreateProductCommandHandlerTests()
    {
        _userContext.SetupGet(u => u.UserId).Returns(_testUserId);
        _userContext.SetupGet(u => u.IsAuthenticated).Returns(true);

        _handler = new CreateProductCommandHandler(
            _productRepository.Object,
            _categoryRepository.Object,
            _brandRepository.Object,
            _attributeDefinitionRepository.Object,
            _outboxWriter.Object,
            _userContext.Object);
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
    public async Task Handle_WithValidCommand_CallsAddAsyncOnce()
    {
        // Arrange
        SetupHappyPath();
        var command = ValidCommand();

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _productRepository.Verify(
            r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()),
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
        var command = ValidCommand();

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

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

        // Assert
        _productRepository.Verify(
            r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSlugInUse_DoesNotCallOutboxWriter()
    {
        // Arrange
        SetupHappyPath();
        _productRepository
            .Setup(r => r.SlugInUseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _handler.Handle(ValidCommand(), CancellationToken.None);

        // Assert
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
    public async Task Handle_WhenBrandIdProvidedButBrandNotFound_ReturnsBrandNotFoundError()
    {
        // Arrange
        SetupHappyPath();
        var brandId = Guid.NewGuid();
        _brandRepository
            .Setup(r => r.GetByIdAsync(brandId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Brand?)null);
        var command = ValidCommand() with { BrandId = brandId };

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.BrandNotFound(brandId).Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenBrandIdIsNull_DoesNotCallBrandRepository()
    {
        // Arrange
        SetupHappyPath();
        var command = ValidCommand();

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _brandRepository.Verify(
            r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenVariantHasAttributes_CallsGetOrCreateAsyncPerAttribute()
    {
        // Arrange
        SetupHappyPath();
        var attributeDefinition = new AttributeDefinition { Id = Guid.NewGuid(), Name = "Color" };
        _attributeDefinitionRepository
            .Setup(r => r.GetOrCreateAsync(
                It.IsAny<Guid?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(attributeDefinition);

        var command = ValidCommand() with
        {
            Variants =
            [
                new CreateProductVariantItem(
                    Sku: "VAR-001",
                    Name: "Default",
                    Price: 100_000,
                    CompareAtPrice: null,
                    ImageUrl: null,
                    Barcode: null,
                    Attributes:
                    [
                        new AttributeNameValueItem("Color", "Red"),
                        new AttributeNameValueItem("Size", "M")
                    ],
                    GalleryImages: [])
            ]
        };

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — verify called twice (once per attribute)
        _attributeDefinitionRepository.Verify(
            r => r.GetOrCreateAsync(
                It.IsAny<Guid?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_WithMultipleVariants_ReturnsSuccessAndWritesEvent()
    {
        // Arrange
        SetupHappyPath();
        var command = ValidCommand() with
        {
            Variants =
            [
                ValidVariant() with { Sku = "VAR-001" },
                ValidVariant() with { Sku = "VAR-002", Name = "Variant 2" }
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

    [Fact]
    public async Task Handle_WithProductImages_CallsAddAsyncWithImages()
    {
        // Arrange
        SetupHappyPath();
        var command = ValidCommand() with
        {
            ProductImages =
            [
                new CreateProductImageItem("https://example.com/img1.jpg", "Image 1", 0, true),
                new CreateProductImageItem("https://example.com/img2.jpg", "Image 2", 1, false)
            ]
        };

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _productRepository.Verify(
            r => r.AddAsync(It.Is<Product>(p => p.Images.Count == 2), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private void SetupHappyPath()
    {
        var categoryId = Guid.NewGuid();

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
            .ReturnsAsync((Guid id, CancellationToken _) => new Category { Id = id, Name = "Electronics", Slug = "electronics" });

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

