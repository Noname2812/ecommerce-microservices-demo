using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Messaging.Idempotency;

public static class IdempotencyHttpServiceCollectionExtensions
{
    public static IServiceCollection AddHttpIdempotency(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<IdempotencyHttpOptions>(configuration.GetSection(IdempotencyHttpOptions.SectionName));
        return services;
    }

    public static IServiceCollection AddHttpIdempotency(
        this IServiceCollection services,
        Action<IdempotencyHttpOptions> configure)
    {
        services.Configure(configure);
        return services;
    }
}
