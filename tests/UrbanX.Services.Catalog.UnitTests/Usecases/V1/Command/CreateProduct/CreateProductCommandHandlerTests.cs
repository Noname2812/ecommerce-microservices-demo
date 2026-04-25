using Moq;
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

        var result = await _handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
    }

    [Fact]
    public async Task Handle_WithValidCommand_WritesOutboxOnce()
    {
        SetupHappyPath();

        await _handler.Handle(ValidCommand(), CancellationToken.None);

        _outboxWriter.Verify(
            w => w.WriteAsync(It.IsAny<ProductIntegrationEvents.ProductCreatedV1>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidCommand_CallsRepositoryAddOnce()
    {
        SetupHappyPath();

        await _handler.Handle(ValidCommand(), CancellationToken.None);

        _productRepository.Verify(
            r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenSlugInUse_ReturnsSlugInUseError()
    {
        SetupHappyPath();
        _productRepository
            .Setup(r => r.SlugInUseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.SlugInUse("x").Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenSlugInUse_DoesNotCallRepositoryAdd()
    {
        SetupHappyPath();
        _productRepository
            .Setup(r => r.SlugInUseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _handler.Handle(ValidCommand(), CancellationToken.None);

        _productRepository.Verify(
            r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenProductSkuInUse_ReturnsSkuInUseError()
    {
        SetupHappyPath();
        _productRepository
            .Setup(r => r.SkuInUseAsync("SKU-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.SkuInUse("SKU-001").Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenVariantSkuInUse_ReturnsSkuInUseError()
    {
        SetupHappyPath();
        _productRepository
            .Setup(r => r.SkuInUseAsync("VAR-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.SkuInUse("VAR-001").Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenCategoryNotFound_ReturnsCategoryNotFoundError()
    {
        SetupHappyPath();
        _categoryRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        var result = await _handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.CategoryNotFound(Guid.Empty).Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenBrandIdProvidedButBrandNotFound_ReturnsBrandNotFoundError()
    {
        SetupHappyPath();
        var brandId = Guid.NewGuid();
        _brandRepository
            .Setup(r => r.GetByIdAsync(brandId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Brand?)null);

        var command = ValidCommand() with { BrandId = brandId };
        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.BrandNotFound(brandId).Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenBrandIdIsNull_DoesNotCallBrandRepository()
    {
        SetupHappyPath();

        await _handler.Handle(ValidCommand(), CancellationToken.None);

        _brandRepository.Verify(
            r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenVariantHasAttributes_CallsGetOrCreatePerAttribute()
    {
        SetupHappyPath();
        _attributeDefinitionRepository
            .Setup(r => r.GetOrCreateAsync(
                It.IsAny<Guid?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttributeDefinition { Id = Guid.NewGuid(), Name = "Color" });

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

        await _handler.Handle(command, CancellationToken.None);

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
            .ReturnsAsync(new Category { Id = Guid.NewGuid(), Name = "Electronics", Slug = "electronics" });
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
