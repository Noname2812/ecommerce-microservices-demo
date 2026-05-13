using Moq;
using Shared.Application.Authorization;
using Shared.Contract.Messaging.Catalog;
using Shared.Outbox.Abstractions;
using UrbanX.Catalog.Application.Usecases.V1.Command.UpdateProductBasicInfo;
using UrbanX.Catalog.Domain.Errors;
using UrbanX.Catalog.Domain;
using UrbanX.Catalog.Domain.Models;
using UrbanX.Catalog.Domain.ValueObjects;

namespace UrbanX.Services.Catalog.UnitTests.Usecases.V1.Command.UpdateProductBasicInfo;

public class UpdateProductBasicInfoCommandHandlerTests
{
    private readonly Mock<IProductRepository> _productRepository = new();
    private readonly Mock<ICategoryRepository> _categoryRepository = new();
    private readonly Mock<IBrandRepository> _brandRepository = new();
    private readonly Mock<IOutboxWriter> _outboxWriter = new();
    private readonly Mock<IUserContext> _userContext = new();

    private readonly UpdateProductBasicInfoCommandHandler _handler;

    public UpdateProductBasicInfoCommandHandlerTests()
    {
        _userContext.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
        _userContext.SetupGet(u => u.IsAuthenticated).Returns(true);
        _userContext.SetupGet(u => u.Scope).Returns(PermissionScope.All);

        _handler = new UpdateProductBasicInfoCommandHandler(
            _productRepository.Object,
            _categoryRepository.Object,
            _brandRepository.Object,
            _outboxWriter.Object,
            _userContext.Object);
    }

    [Fact]
    public async Task Handle_HappyPath_ReturnsSuccessAndEmitsProductInfoUpdatedV1WithActiveVariants()
    {
        // Arrange
        var product = MakeProduct();
        SetupHappyPath(product);
        var command = ValidCommand(product.Id);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _outboxWriter.Verify(
            w => w.WriteAsync(
                It.Is<ProductUpdateIntegrationEvents.ProductInfoUpdatedV1>(e =>
                    e.ProductId == product.Id &&
                    e.ActiveVariants.Count == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenStatusChanges_AlsoEmitsProductStatusChangedV1()
    {
        // Arrange
        var product = MakeProduct(status: ProductStatus.Draft);
        SetupHappyPath(product);
        var command = ValidCommand(product.Id) with { Status = ProductStatus.Active };

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _outboxWriter.Verify(
            w => w.WriteAsync(
                It.IsAny<ProductUpdateIntegrationEvents.ProductInfoUpdatedV1>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _outboxWriter.Verify(
            w => w.WriteAsync(
                It.Is<ProductUpdateIntegrationEvents.ProductStatusChangedV1>(e =>
                    e.OldStatus == ProductStatus.Draft &&
                    e.NewStatus == ProductStatus.Active),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenProductNotFound_ReturnsProductNotFoundError()
    {
        // Arrange
        var productId = Guid.NewGuid();
        _productRepository
            .Setup(r => r.GetByIdForUpdateAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        // Act
        var result = await _handler.Handle(ValidCommand(productId), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(CatalogErrors.ProductNotFound(productId).Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenSlugAlreadyUsedByAnotherProduct_ReturnsSlugExistsError()
    {
        // Arrange
        var product = MakeProduct();
        SetupHappyPath(product);
        _productRepository
            .Setup(r => r.IsSlugInUseExcludingProductAsync(It.IsAny<string>(), product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(ValidCommand(product.Id) with { Slug = "taken-slug" }, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(CatalogErrors.SlugExists("x").Code, result.Error.Code);
    }

    private void SetupHappyPath(Product product)
    {
        _productRepository
            .Setup(r => r.GetByIdForUpdateAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _productRepository
            .Setup(r => r.IsSlugInUseExcludingProductAsync(It.IsAny<string>(), product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _categoryRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new Category { Id = id, Name = "Electronics", Slug = "electronics" });
        _outboxWriter
            .Setup(w => w.WriteAsync(It.IsAny<ProductUpdateIntegrationEvents.ProductInfoUpdatedV1>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _outboxWriter
            .Setup(w => w.WriteAsync(It.IsAny<ProductUpdateIntegrationEvents.ProductStatusChangedV1>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static Product MakeProduct(string status = ProductStatus.Draft)
    {
        var categoryId = Guid.NewGuid();
        return Product.Create(
            sku: "SKU-001",
            name: "Test Product",
            slug: "test-product",
            description: null,
            shortDescription: null,
            categoryId: categoryId,
            brandId: null,
            categoryName: "Electronics",
            brandName: null,
            basePrice: 100_000,
            sellerId: Guid.NewGuid(),
            sellerName: "Test Seller",
            status: status,
            weightGrams: null,
            dimensions: null,
            tags: new List<string>(),
            metaTitle: null,
            metaDescription: null,
            productImages: new List<NewProductImageSpec>(),
            variantSpecs: new List<NewVariantSpec>
            {
                new("VAR-001", "Default", 100_000, null, null, null,
                    new List<(Guid, string)>(),
                    new List<NewProductImageSpec>())
            });
    }

    private static UpdateProductBasicInfoCommand ValidCommand(Guid productId) => new(
        ProductId: productId,
        Name: "Updated Product",
        Slug: "updated-product",
        Description: null,
        ShortDescription: null,
        CategoryId: null,
        BrandId: null,
        BasePrice: 120_000,
        Status: null,
        WeightGrams: null,
        Dimensions: null,
        Tags: null,
        MetaTitle: null,
        MetaDescription: null);
}
