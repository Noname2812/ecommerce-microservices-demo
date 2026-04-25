using Shared.Application;
using Shared.Contract.Dtos.Catalog;
using Shared.Contract.Messaging.Catalog;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;
using UrbanX.Catalog.Application.Abstractions;
using UrbanX.Catalog.Application.Usecases.V1.Errors;
using UrbanX.Catalog.Domain;
using UrbanX.Catalog.Domain.Models;
using UrbanX.Catalog.Domain.ValueObjects;

namespace UrbanX.Catalog.Application.Usecases.V1.Command.UpdateProductVariants
{
    public sealed class UpdateProductVariantsCommandHandler : ICommandHandler<UpdateProductVariantsCommand>
    {
        private readonly IProductRepository _productRepository;
        private readonly IOutboxWriter _outboxWriter;
        private readonly IInventoryServiceClient _inventoryServiceClient;

        public UpdateProductVariantsCommandHandler(
            IProductRepository productRepository,
            IOutboxWriter outboxWriter,
            IInventoryServiceClient inventoryServiceClient)
        {
            _productRepository = productRepository;
            _outboxWriter = outboxWriter;
            _inventoryServiceClient = inventoryServiceClient;
        }

        public async Task<Result> Handle(UpdateProductVariantsCommand request, CancellationToken cancellationToken)
        {
            var product = await _productRepository.GetByIdForUpdateAsync(request.ProductId, cancellationToken);
            if (product is null)
                return Result.Failure(CatalogErrors.ProductNotFound(request.ProductId));

            var utcNow = DateTimeOffset.UtcNow;
            var dbVariants = product.Variants.Where(v => v.DeletedAt == null).ToList();
            var snapshotIds = request.Variants
                .Where(v => v.Id.HasValue)
                .Select(v => v.Id!.Value)
                .ToHashSet();

            var toAdd = request.Variants.Where(v => v.Id is null).ToList();
            var toUpdate = request.Variants.Where(v => v.Id.HasValue).ToList();
            var toDelete = dbVariants.Where(v => !snapshotIds.Contains(v.Id)).ToList();

            if (!request.Variants.Any(v => v.IsActive))
                return Result.Failure(CatalogErrors.NoActiveVariant());

            var dbVariantIds = dbVariants.Select(v => v.Id).ToHashSet();
            foreach (var item in toUpdate)
            {
                if (!dbVariantIds.Contains(item.Id!.Value))
                    return Result.Failure(CatalogErrors.VariantNotFound(item.Id!.Value));
            }

            foreach (var variant in toDelete)
            {
                var invStatus = await _inventoryServiceClient.GetVariantInventoryStatusAsync(variant.Id, cancellationToken);
                if (invStatus is null)
                    return Result.Failure(CatalogErrors.InventoryCheckUnavailable());
                if (invStatus.HasActiveReservations)
                    return Result.Failure(CatalogErrors.VariantHasActiveReservations());
            }

            foreach (var item in toUpdate.Concat(toAdd))
            {
                if (await _productRepository.IsSkuInUseExcludingAsync(item.Sku, product.Id, item.Id, cancellationToken))
                    return Result.Failure(CatalogErrors.SkuExists(item.Sku));
            }

            // Apply deletions
            var deletedEvents = new List<ProductUpdateIntegrationEvents.ProductVariantDeletedV1>();
            foreach (var variant in toDelete)
            {
                variant.MarkSoftDeleted(utcNow);
                deletedEvents.Add(new ProductUpdateIntegrationEvents.ProductVariantDeletedV1(
                    product.Id, variant.Id, variant.Sku));
            }

            // Apply updates
            var updatedEvents = new List<ProductUpdateIntegrationEvents.ProductVariantUpdatedV1>();
            var dbVariantById = dbVariants.ToDictionary(v => v.Id);
            foreach (var item in toUpdate)
            {
                var variant = dbVariantById[item.Id!.Value];
                string? prevSku = null;
                decimal? prevPrice = null;
                bool? prevIsActive = null;

                if (!string.Equals(variant.Sku, item.Sku, StringComparison.OrdinalIgnoreCase))
                {
                    prevSku = variant.Sku;
                    await _productRepository.AddSkuHistoryAsync(new VariantSkuHistory
                    {
                        Id = Guid.NewGuid(),
                        VariantId = variant.Id,
                        OldSku = variant.Sku,
                        NewSku = item.Sku,
                        ChangedBy = Guid.Empty,
                        CreatedAt = utcNow
                    }, cancellationToken);
                    variant.SetSku(item.Sku);
                }

                if (variant.Price != item.Price || variant.CompareAtPrice != item.CompareAtPrice)
                {
                    prevPrice = variant.Price;
                    await _productRepository.AddPriceHistoryAsync(new VariantPriceHistory
                    {
                        Id = Guid.NewGuid(),
                        VariantId = variant.Id,
                        OldPrice = variant.Price,
                        NewPrice = item.Price,
                        OldCompareAt = variant.CompareAtPrice,
                        NewCompareAt = item.CompareAtPrice,
                        ChangedById = Guid.Empty,
                        ChangedByName = string.Empty,
                        CreatedAt = utcNow
                    }, cancellationToken);
                    variant.SetPrice(item.Price, item.CompareAtPrice);
                }

                if (variant.IsActive != item.IsActive)
                {
                    prevIsActive = variant.IsActive;
                    variant.SetIsActive(item.IsActive);
                }

                variant.SetName(item.Name);
                variant.SetImageUrl(item.ImageUrl);
                variant.SetBarcode(item.Barcode);

                if (prevSku is not null || prevPrice is not null || prevIsActive is not null)
                {
                    updatedEvents.Add(new ProductUpdateIntegrationEvents.ProductVariantUpdatedV1(
                        product.Id,
                        product.SellerId,
                        variant.Id,
                        prevSku,
                        prevPrice,
                        prevIsActive,
                        BuildVariantSnapshot(variant, product)));
                }
            }

            // Apply additions
            var addedEvents = new List<ProductUpdateIntegrationEvents.ProductVariantAddedV1>();
            foreach (var item in toAdd)
            {
                var attrValues = (item.AttributeValues ?? [])
                    .Select(a => (a.AttributeDefinitionId, a.Value))
                    .ToList();

                var galleryImages = (item.GalleryImages ?? [])
                    .Select(g => new NewProductImageSpec(g.Url, g.AltText, g.DisplayOrder, g.IsPrimary))
                    .ToList();

                product.AddVariant(
                    new NewVariantSpec(item.Sku, item.Name, item.Price, item.CompareAtPrice,
                                       item.ImageUrl, item.Barcode, attrValues, galleryImages),
                    galleryImages,
                    utcNow);

                var added = product.Variants.Last();
                var variantSnapshot = new ProductDtos.ProductVariantSnapshot(
                    added.Id,
                    added.Sku,
                    added.Name,
                    added.Price,
                    added.CompareAtPrice,
                    added.ImageUrl,
                    added.Barcode,
                    added.IsActive,
                    (item.AttributeValues ?? [])
                        .Select(a => new ProductDtos.ProductVariantAttributeSnapshot(
                            a.AttributeDefinitionId.ToString(), a.AttributeDefinitionId, a.Value))
                        .ToList(),
                    galleryImages.Select(g => g.Url).ToList());

                addedEvents.Add(new ProductUpdateIntegrationEvents.ProductVariantAddedV1(
                    product.Id, product.SellerId, variantSnapshot));
            }

            foreach (var e in deletedEvents)
                await _outboxWriter.WriteAsync(e, cancellationToken);
            foreach (var e in updatedEvents)
                await _outboxWriter.WriteAsync(e, cancellationToken);
            foreach (var e in addedEvents)
                await _outboxWriter.WriteAsync(e, cancellationToken);

            return Result.Success();
        }

        private static ProductDtos.ProductVariantSnapshot BuildVariantSnapshot(ProductVariant v, Product product)
        {
            var imageUrls = new List<string>();
            if (!string.IsNullOrWhiteSpace(v.ImageUrl)) imageUrls.Add(v.ImageUrl!);
            imageUrls.AddRange(
                product.Images
                    .Where(i => i.VariantId == v.Id)
                    .OrderBy(i => i.DisplayOrder)
                    .Select(i => i.Url));

            return new ProductDtos.ProductVariantSnapshot(
                v.Id, v.Sku, v.Name, v.Price, v.CompareAtPrice,
                v.ImageUrl, v.Barcode, v.IsActive,
                v.AttributeValues
                    .Select(av => new ProductDtos.ProductVariantAttributeSnapshot(
                        av.AttributeDefinition?.Name ?? av.AttributeId.ToString(),
                        av.AttributeId, av.Value))
                    .ToList(),
                imageUrls.Distinct().ToList());
        }
    }
}
