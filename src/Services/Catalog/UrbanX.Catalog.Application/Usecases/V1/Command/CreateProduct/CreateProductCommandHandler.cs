using Shared.Application;
using Shared.Contract.Common;
using Shared.Contract.Dtos.Catalog;
using Shared.Contract.Messaging.Catalog;
using Shared.Outbox.Abstractions;
using UrbanX.Catalog.Application.Usecases.V1.Errors;
using UrbanX.Catalog.Domain;
using UrbanX.Catalog.Domain.Helpers;
using UrbanX.Catalog.Domain.Models;
using UrbanX.Catalog.Domain.ValueObjects;

namespace UrbanX.Catalog.Application.Usecases.V1.Command
{
    public sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand>
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

        public async Task<Result> Handle(CreateProductCommand request, CancellationToken cancellationToken)
        {
            var slug = string.IsNullOrWhiteSpace(request.Slug)
                ? SlugHelper.ToSlug(request.Name)
                : request.Slug!.Trim().ToLowerInvariant();

            if (await _productRepository.SlugInUseAsync(slug, cancellationToken))
                return Result.Failure(ProductErrors.SlugInUse(slug));

            if (await _productRepository.SkuInUseAsync(request.Sku, cancellationToken))
                return Result.Failure(ProductErrors.SkuInUse(request.Sku));
            foreach (var v in request.Variants)
            {
                if (await _productRepository.SkuInUseAsync(v.Sku, cancellationToken))
                    return Result.Failure(ProductErrors.SkuInUse(v.Sku));
            }

            var category = await _categoryRepository.GetByIdAsync(request.CategoryId, cancellationToken);
            if (category is null)
                return Result.Failure(ProductErrors.CategoryNotFound(request.CategoryId));

            Brand? brand = null;
            if (request.BrandId is { } brandId)
            {
                brand = await _brandRepository.GetByIdAsync(brandId, cancellationToken);
                if (brand is null)
                    return Result.Failure(ProductErrors.BrandNotFound(brandId));
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
                        request.CategoryId,
                        a.Name,
                        AttributeValueTypes.Text,
                        isVariant: true,
                        displayOrder: displayOrder++,
                        cancellationToken);
                    attributeNameById[def.Id] = def.Name;
                    valuePairs.Add((def.Id, a.Value));
                }

                var gallery = v.GalleryImages
                    .Select(
                        g => new NewProductImageSpec(
                            g.Url,
                            g.AltText,
                            g.DisplayOrder,
                            g.IsPrimary))
                    .ToList();

                variantSpecs.Add(
                    new NewVariantSpec(
                        v.Sku,
                        v.Name,
                        v.Price,
                        v.CompareAtPrice,
                        v.ImageUrl,
                        v.Barcode,
                        valuePairs,
                        gallery));
            }

            var productImages = request.ProductImages
                .Select(
                    p => new NewProductImageSpec(
                        p.Url,
                        p.AltText,
                        p.DisplayOrder,
                        p.IsPrimary))
                .ToList();

            ProductDimensions? dimensions = null;
            if (request.Dimensions is { } d)
            {
                dimensions = new ProductDimensions
                {
                    LengthCm = d.LengthCm, WidthCm = d.WidthCm, HeightCm = d.HeightCm
                };
            }

            var product = Product.Create(
                request.Sku,
                request.Name,
                slug,
                request.Description,
                request.ShortDescription,
                request.CategoryId,
                request.BrandId,
                category.Name,
                brand?.Name,
                request.BasePrice,
                request.SellerId,
                request.SellerName,
                request.Status ?? ProductStatus.Draft,
                request.WeightGrams,
                dimensions,
                request.Tags?.ToList() ?? new List<string>(),
                request.MetaTitle,
                request.MetaDescription,
                productImages,
                variantSpecs);

            await _productRepository.AddAsync(product, cancellationToken);

            var integrationEvent = MapToCreatedEvent(product, attributeNameById);
            await _outboxWriter.WriteAsync(integrationEvent, cancellationToken);

            return Result.Success();
        }

        private static ProductIntegrationEvents.ProductCreatedV1 MapToCreatedEvent(
            Product product,
            IReadOnlyDictionary<Guid, string> attributeNameById)
        {
            return new ProductIntegrationEvents.ProductCreatedV1(
                product.Id,
                product.Sku,
                product.Name,
                product.Slug,
                product.Description,
                product.ShortDescription,
                product.CategoryId,
                product.BrandId,
                product.CategoryName,
                product.BrandName,
                product.BasePrice,
                product.SellerId,
                product.SellerName,
                product.Status,
                product.Tags,
                product.Dimensions is null
                    ? null
                    : new ProductDtos.ProductDimensionsSnapshot(
                        product.Dimensions.LengthCm,
                        product.Dimensions.WidthCm,
                        product.Dimensions.HeightCm),
                product.Variants
                    .Select(
                        v => new ProductDtos.ProductVariantSnapshot(
                            v.Id,
                            v.Sku,
                            v.Name,
                            v.Price,
                            v.CompareAtPrice,
                            v.ImageUrl,
                            v.Barcode,
                            v.IsActive,
                            v.AttributeValues
                                .Select(
                                    av => new ProductDtos.ProductVariantAttributeSnapshot(
                                        attributeNameById[av.AttributeId],
                                        av.AttributeId,
                                        av.Value))
                                .ToList(),
                            BuildVariantImageUrlList(v, product)))
                    .ToList());
        }

        private static List<string> BuildVariantImageUrlList(ProductVariant v, Product product)
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
