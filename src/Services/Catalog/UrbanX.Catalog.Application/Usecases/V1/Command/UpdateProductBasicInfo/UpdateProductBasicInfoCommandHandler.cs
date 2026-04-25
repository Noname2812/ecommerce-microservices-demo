using Shared.Application;
using Shared.Contract.Dtos.Catalog;
using Shared.Contract.Messaging.Catalog;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;
using UrbanX.Catalog.Application.Usecases.V1.Errors;
using UrbanX.Catalog.Domain;
using UrbanX.Catalog.Domain.Helpers;
using UrbanX.Catalog.Domain.Models;
using UrbanX.Catalog.Domain.ValueObjects;

namespace UrbanX.Catalog.Application.Usecases.V1.Command.UpdateProductBasicInfo
{
    public sealed class UpdateProductBasicInfoCommandHandler : ICommandHandler<UpdateProductBasicInfoCommand>
    {
        private readonly IProductRepository _productRepository;
        private readonly ICategoryRepository _categoryRepository;
        private readonly IBrandRepository _brandRepository;
        private readonly IOutboxWriter _outboxWriter;

        public UpdateProductBasicInfoCommandHandler(
            IProductRepository productRepository,
            ICategoryRepository categoryRepository,
            IBrandRepository brandRepository,
            IOutboxWriter outboxWriter)
        {
            _productRepository = productRepository;
            _categoryRepository = categoryRepository;
            _brandRepository = brandRepository;
            _outboxWriter = outboxWriter;
        }

        public async Task<Result> Handle(UpdateProductBasicInfoCommand request, CancellationToken cancellationToken)
        {
            var product = await _productRepository.GetByIdForUpdateAsync(request.ProductId, cancellationToken);
            if (product is null)
                return Result.Failure(CatalogErrors.ProductNotFound(request.ProductId));

            var slug = string.IsNullOrWhiteSpace(request.Slug)
                ? SlugHelper.ToSlug(request.Name)
                : request.Slug!.Trim().ToLowerInvariant();

            if (await _productRepository.IsSlugInUseExcludingProductAsync(slug, product.Id, cancellationToken))
                return Result.Failure(CatalogErrors.SlugExists(slug));

            string? categoryName = product.CategoryName;
            if (request.CategoryId is { } newCategoryId && newCategoryId != product.CategoryId)
            {
                var category = await _categoryRepository.GetByIdAsync(newCategoryId, cancellationToken);
                if (category is null)
                    return Result.Failure(ProductErrors.CategoryNotFound(newCategoryId));
                categoryName = category.Name;
            }
            else if (request.CategoryId is null)
            {
                categoryName = null;
            }

            string? brandName = product.BrandName;
            if (request.BrandId is { } newBrandId && newBrandId != product.BrandId)
            {
                var brand = await _brandRepository.GetByIdAsync(newBrandId, cancellationToken);
                if (brand is null)
                    return Result.Failure(ProductErrors.BrandNotFound(newBrandId));
                brandName = brand.Name;
            }
            else if (request.BrandId is null)
            {
                brandName = null;
            }

            ProductDimensions? dimensions = null;
            if (request.Dimensions is { } d)
                dimensions = new ProductDimensions { LengthCm = d.LengthCm, WidthCm = d.WidthCm, HeightCm = d.HeightCm };

            var oldStatus = product.Status;
            var utcNow = DateTimeOffset.UtcNow;

            product.ApplyEdit(new ProductEditState
            {
                Name = request.Name,
                Slug = slug,
                Description = request.Description,
                ShortDescription = request.ShortDescription,
                CategoryId = request.CategoryId,
                CategoryName = categoryName,
                BrandId = request.BrandId,
                BrandName = brandName,
                BasePrice = request.BasePrice,
                Tags = request.Tags?.ToList() ?? new List<string>(),
                WeightGrams = request.WeightGrams,
                Dimensions = dimensions,
                MetaTitle = request.MetaTitle,
                MetaDescription = request.MetaDescription,
                Status = request.Status
            }, utcNow);

            var activeVariants = BuildActiveVariantSnapshots(product);
            var primaryImageUrl = product.Images
                .Where(i => i.VariantId is null && i.IsPrimary)
                .OrderBy(i => i.DisplayOrder)
                .Select(i => i.Url)
                .FirstOrDefault();

            await _outboxWriter.WriteAsync(new ProductUpdateIntegrationEvents.ProductInfoUpdatedV1(
                product.Id,
                product.SellerId,
                new ProductDtos.ProductUpdateSnapshot(
                    product.Id,
                    product.Sku,
                    product.Name,
                    product.Slug,
                    product.Description,
                    product.ShortDescription,
                    product.CategoryId,
                    product.CategoryName,
                    product.BrandId,
                    product.BrandName,
                    product.SellerId,
                    product.SellerName,
                    product.BasePrice,
                    product.Status,
                    product.Tags,
                    product.WeightGrams,
                    product.Dimensions is null
                        ? null
                        : new ProductDtos.ProductDimensionsSnapshot(
                            product.Dimensions.LengthCm,
                            product.Dimensions.WidthCm,
                            product.Dimensions.HeightCm),
                    product.MetaTitle,
                    product.MetaDescription,
                    primaryImageUrl),
                activeVariants), cancellationToken);

            if (product.Status != oldStatus)
            {
                await _outboxWriter.WriteAsync(new ProductUpdateIntegrationEvents.ProductStatusChangedV1(
                    product.Id,
                    oldStatus,
                    product.Status,
                    null,
                    product.Variants
                        .Where(v => v.DeletedAt == null)
                        .Select(v => v.Id)
                        .ToList()), cancellationToken);
            }

            return Result.Success();
        }

        private static List<ProductDtos.ProductVariantSnapshot> BuildActiveVariantSnapshots(Product product)
        {
            return product.Variants
                .Where(v => v.DeletedAt == null && v.IsActive)
                .Select(v => new ProductDtos.ProductVariantSnapshot(
                    v.Id,
                    v.Sku,
                    v.Name,
                    v.Price,
                    v.CompareAtPrice,
                    v.ImageUrl,
                    v.Barcode,
                    v.IsActive,
                    v.AttributeValues
                        .Select(av => new ProductDtos.ProductVariantAttributeSnapshot(
                            av.AttributeDefinition?.Name ?? av.AttributeId.ToString(),
                            av.AttributeId,
                            av.Value))
                        .ToList(),
                    BuildVariantImageUrls(v, product)))
                .ToList();
        }

        private static List<string> BuildVariantImageUrls(ProductVariant v, Product product)
        {
            var urls = new List<string>();
            if (!string.IsNullOrWhiteSpace(v.ImageUrl))
                urls.Add(v.ImageUrl!);
            urls.AddRange(
                product.Images
                    .Where(i => i.VariantId == v.Id)
                    .OrderBy(i => i.DisplayOrder)
                    .Select(i => i.Url));
            return urls.Distinct().ToList();
        }
    }
}
