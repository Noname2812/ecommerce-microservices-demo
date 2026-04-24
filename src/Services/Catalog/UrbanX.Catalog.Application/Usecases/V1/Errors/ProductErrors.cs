using Shared.Kernel.Primitives;

namespace UrbanX.Catalog.Application.Usecases.V1.Errors
{
    public static class ProductErrors
    {
        public static Error NotFound(Guid id) =>
            new("Product.NotFound", $"Product {id} not found");

        public static Error CategoryNotFound(Guid id) =>
            new("Category.NotFound", $"Category {id} was not found");

        public static Error BrandNotFound(Guid id) =>
            new("Brand.NotFound", $"Brand {id} was not found");

        public static Error SkuInUse(string sku) =>
            new("Product.SkuInUse", $"The SKU \"{sku}\" is already in use");

        public static Error SlugInUse(string slug) =>
            new("Product.SlugInUse", $"The slug \"{slug}\" is already in use");
    }
}
