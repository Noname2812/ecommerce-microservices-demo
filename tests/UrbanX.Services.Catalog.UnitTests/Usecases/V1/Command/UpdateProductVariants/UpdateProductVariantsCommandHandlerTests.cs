using Moq;
using Shared.Application.Authorization;
using Shared.Contract.Messaging.Catalog;
using Shared.Outbox.Abstractions;
using UrbanX.Catalog.Application.Abstractions;
using UrbanX.Catalog.Application.Usecases.V1.Command.UpdateProductVariants;
using UrbanX.Catalog.Domain.Errors;
using UrbanX.Catalog.Domain;
using UrbanX.Catalog.Domain.Models;
using UrbanX.Catalog.Domain.ValueObjects;

namespace UrbanX.Services.Catalog.UnitTests.Usecases.V1.Command.UpdateProductVariants;

public class UpdateProductVariantsCommandHandlerTests
{
    private readonly Mock<IProductRepository> _productRepository = new();
    private readonly Mock<IOutboxWriter> _outboxWriter = new();
    private readonly Mock<IInventoryServiceClient> _inventoryServiceClient = new();
    private readonly Mock<IUserContext> _userContext = new();

    private readonly UpdateProductVariantsCommandHandler _handler;

    public UpdateProductVariantsCommandHandlerTests()
    {
        _userContext.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
        _userContext.SetupGet(u => u.IsAuthenticated).Returns(true);
        _userContext.SetupGet(u => u.Scope).Returns(PermissionScope.All);

        _handler = new UpdateProductVariantsCommandHandler(
            _productRepository.Object,
            _outboxWriter.Object,
            _inventoryServiceClient.Object,
            _userContext.Object);
    }

    [Fact]
    public async Task Handle_HappyPath_EmitsDeletedUpdatedAndAddedEvents()
    {
        // Arrange: product with 2 variants: V1 (will be deleted), V2 (will be updated, SKU changes)
        var product = MakeProduct(variantCount: 2);
        var v1 = product.Variants[0];
        var v2 = product.Variants[1];

        SetupHappyPath(product);
        _inventoryServiceClient
            .Setup(c => c.GetVariantInventoryStatusAsync(v1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VariantInventoryStatus(0, false));

        var command = new UpdateProductVariantsCommand(
            ProductId: product.Id,
            Variants:
            [
                // V2 updated with new SKU
                new VariantSnapshotItem(v2.Id, "NEW-SKU-V2", null, 150_000, null, null, null, true, null, null),
                // new variant added
                new VariantSnapshotItem(null, "BRAND-NEW", null, 200_000, null, null, null, true, null, null)
            ]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _outboxWriter.Verify(
            w => w.WriteAsync(It.IsAny<ProductUpdateIntegrationEvents.ProductVariantDeletedV1>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _outboxWriter.Verify(
            w => w.WriteAsync(It.IsAny<ProductUpdateIntegrationEvents.ProductVariantUpdatedV1>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _outboxWriter.Verify(
            w => w.WriteAsync(It.IsAny<ProductUpdateIntegrationEvents.ProductVariantAddedV1>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenSnapshotHasNoActiveVariant_ReturnsNoActiveVariantError()
    {
        // Arrange: product with 1 active variant; snapshot sets it to inactive
        var product = MakeProduct(variantCount: 1);
        var v1 = product.Variants[0];
        SetupHappyPath(product);

        var command = new UpdateProductVariantsCommand(
            ProductId: product.Id,
            Variants: [new VariantSnapshotItem(v1.Id, v1.Sku, null, v1.Price, null, null, null, false, null, null)]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(CatalogErrors.NoActiveVariant().Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenSnapshotContainsUnknownVariantId_ReturnsVariantNotFoundError()
    {
        // Arrange
        var product = MakeProduct(variantCount: 1);
        SetupHappyPath(product);
        var unknownId = Guid.NewGuid();

        var command = new UpdateProductVariantsCommand(
            ProductId: product.Id,
            Variants: [new VariantSnapshotItem(unknownId, "SKU-X", null, 100_000, null, null, null, true, null, null)]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(CatalogErrors.VariantNotFound(unknownId).Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenDeletedVariantHasActiveReservation_ReturnsVariantHasActiveReservationsError()
    {
        // Arrange: product with 2 variants; V1 will be deleted; V1 has active reservation
        var product = MakeProduct(variantCount: 2);
        var v1 = product.Variants[0];
        var v2 = product.Variants[1];

        SetupHappyPath(product);
        _inventoryServiceClient
            .Setup(c => c.GetVariantInventoryStatusAsync(v1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VariantInventoryStatus(5, true));

        var command = new UpdateProductVariantsCommand(
            ProductId: product.Id,
            Variants: [new VariantSnapshotItem(v2.Id, v2.Sku, null, v2.Price, null, null, null, true, null, null)]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(CatalogErrors.VariantHasActiveReservations().Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenInventoryServiceUnavailable_ReturnsInventoryCheckUnavailableError()
    {
        // Arrange: product with 2 variants; V1 will be deleted; Inventory returns null
        var product = MakeProduct(variantCount: 2);
        var v1 = product.Variants[0];
        var v2 = product.Variants[1];

        SetupHappyPath(product);
        _inventoryServiceClient
            .Setup(c => c.GetVariantInventoryStatusAsync(v1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((VariantInventoryStatus?)null);

        var command = new UpdateProductVariantsCommand(
            ProductId: product.Id,
            Variants: [new VariantSnapshotItem(v2.Id, v2.Sku, null, v2.Price, null, null, null, true, null, null)]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(CatalogErrors.InventoryCheckUnavailable().Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenSkuAlreadyInUseInDb_ReturnsSkuExistsError()
    {
        // Arrange: updating existing variant to a SKU that's already taken in DB
        var product = MakeProduct(variantCount: 1);
        var v1 = product.Variants[0];
        SetupHappyPath(product);

        _productRepository
            .Setup(r => r.IsSkuInUseExcludingAsync("TAKEN-SKU", product.Id, v1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var command = new UpdateProductVariantsCommand(
            ProductId: product.Id,
            Variants: [new VariantSnapshotItem(v1.Id, "TAKEN-SKU", null, v1.Price, null, null, null, true, null, null)]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(CatalogErrors.SkuExists("TAKEN-SKU").Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenSkuChangedInToUpdate_CallsAddSkuHistoryAndEmitsPreviousSku()
    {
        // Arrange
        var product = MakeProduct(variantCount: 1);
        var v1 = product.Variants[0];
        var originalSku = v1.Sku;
        SetupHappyPath(product);

        var command = new UpdateProductVariantsCommand(
            ProductId: product.Id,
            Variants: [new VariantSnapshotItem(v1.Id, "CHANGED-SKU", null, v1.Price, null, null, null, true, null, null)]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _productRepository.Verify(
            r => r.AddSkuHistoryAsync(
                It.Is<VariantSkuHistory>(h => h.OldSku == originalSku && h.NewSku == "CHANGED-SKU"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _outboxWriter.Verify(
            w => w.WriteAsync(
                It.Is<ProductUpdateIntegrationEvents.ProductVariantUpdatedV1>(e =>
                    e.PreviousSku == originalSku),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPriceChangedInToUpdate_CallsAddPriceHistoryAndEmitsPreviousPrice()
    {
        // Arrange
        var product = MakeProduct(variantCount: 1);
        var v1 = product.Variants[0];
        var originalPrice = v1.Price;
        SetupHappyPath(product);

        var command = new UpdateProductVariantsCommand(
            ProductId: product.Id,
            Variants: [new VariantSnapshotItem(v1.Id, v1.Sku, null, 999_000, null, null, null, true, null, null)]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _productRepository.Verify(
            r => r.AddPriceHistoryAsync(
                It.Is<VariantPriceHistory>(h => h.OldPrice == originalPrice && h.NewPrice == 999_000),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _outboxWriter.Verify(
            w => w.WriteAsync(
                It.Is<ProductUpdateIntegrationEvents.ProductVariantUpdatedV1>(e =>
                    e.PreviousPrice == originalPrice),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenSnapshotIsAllNewItems_DeletesAllDbVariantsAndAddsNew()
    {
        // Arrange: product with 2 DB variants; snapshot has 2 new items (full replace)
        var product = MakeProduct(variantCount: 2);
        var v1 = product.Variants[0];
        var v2 = product.Variants[1];

        SetupHappyPath(product);
        _inventoryServiceClient
            .Setup(c => c.GetVariantInventoryStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VariantInventoryStatus(0, false));

        var command = new UpdateProductVariantsCommand(
            ProductId: product.Id,
            Variants:
            [
                new VariantSnapshotItem(null, "NEW-1", null, 100_000, null, null, null, true, null, null),
                new VariantSnapshotItem(null, "NEW-2", null, 200_000, null, null, null, true, null, null)
            ]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _outboxWriter.Verify(
            w => w.WriteAsync(It.IsAny<ProductUpdateIntegrationEvents.ProductVariantDeletedV1>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _outboxWriter.Verify(
            w => w.WriteAsync(It.IsAny<ProductUpdateIntegrationEvents.ProductVariantAddedV1>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        Assert.True(v1.IsDeleted());
        Assert.True(v2.IsDeleted());
    }

    private void SetupHappyPath(Product product)
    {
        _productRepository
            .Setup(r => r.GetByIdForUpdateAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _productRepository
            .Setup(r => r.IsSkuInUseExcludingAsync(It.IsAny<string>(), product.Id, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _productRepository
            .Setup(r => r.AddSkuHistoryAsync(It.IsAny<VariantSkuHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _productRepository
            .Setup(r => r.AddPriceHistoryAsync(It.IsAny<VariantPriceHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _outboxWriter
            .Setup(w => w.WriteAsync(It.IsAny<ProductUpdateIntegrationEvents.ProductVariantDeletedV1>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _outboxWriter
            .Setup(w => w.WriteAsync(It.IsAny<ProductUpdateIntegrationEvents.ProductVariantUpdatedV1>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _outboxWriter
            .Setup(w => w.WriteAsync(It.IsAny<ProductUpdateIntegrationEvents.ProductVariantAddedV1>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static Product MakeProduct(int variantCount = 1)
    {
        var specs = Enumerable.Range(1, variantCount)
            .Select(i => new NewVariantSpec(
                $"VAR-{i:000}", $"Variant {i}", 100_000m * i, null, null, null,
                new List<(Guid, string)>(),
                new List<NewProductImageSpec>()))
            .ToList();

        return Product.Create(
            sku: "SKU-001",
            name: "Test Product",
            slug: "test-product",
            description: null,
            shortDescription: null,
            categoryId: Guid.NewGuid(),
            brandId: null,
            categoryName: "Electronics",
            brandName: null,
            basePrice: 100_000,
            sellerId: Guid.NewGuid(),
            sellerName: "Test Seller",
            status: ProductStatus.Draft,
            weightGrams: null,
            dimensions: null,
            tags: new List<string>(),
            metaTitle: null,
            metaDescription: null,
            productImages: new List<NewProductImageSpec>(),
            variantSpecs: specs);
    }
}
