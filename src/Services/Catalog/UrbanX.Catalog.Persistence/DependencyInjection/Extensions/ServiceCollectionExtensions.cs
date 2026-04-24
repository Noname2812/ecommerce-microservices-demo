using Microsoft.Extensions.DependencyInjection;
using UrbanX.Catalog.Domain;

namespace UrbanX.Catalog.Persistence.DependencyInjection.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static void AddPersistence(this IServiceCollection services)
        {
            services.AddScoped<IProductRepository, ProductRepository>();
            services.AddScoped<ICategoryRepository, CategoryRepository>();
            services.AddScoped<IBrandRepository, BrandRepository>();
            services.AddScoped<IAttributeDefinitionRepository, AttributeDefinitionRepository>();
        }
    }
}
