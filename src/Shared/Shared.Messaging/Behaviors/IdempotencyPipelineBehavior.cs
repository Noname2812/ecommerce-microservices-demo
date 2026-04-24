using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Shared.Application;
using System.Text.Json;

namespace Shared.Messaging.Behaviors
{
    /// <summary>
    /// Pipeline behavior that enforces idempotency for commands implementing IIdempotentCommand.
    /// Requires IDistributedCache to be registered (Redis recommended for production).
    /// </summary>
    public sealed class IdempotencyPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IIdempotentCommand
    {
        private readonly IDistributedCache _cache;
        private readonly ILogger<IdempotencyPipelineBehavior<TRequest, TResponse>> _logger;
        private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

        public IdempotencyPipelineBehavior(
            IDistributedCache cache,
            ILogger<IdempotencyPipelineBehavior<TRequest, TResponse>> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            var cacheKey = $"idempotency:{typeof(TRequest).Name}:{request.IdempotencyKey}";

            var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
            if (cached is not null)
            {
                _logger.LogInformation(
                    "Idempotency hit for {RequestName} key={Key}. Returning cached response.",
                    typeof(TRequest).Name, request.IdempotencyKey);

                return JsonSerializer.Deserialize<TResponse>(cached)!;
            }

            var response = await next();

            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(response),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = DefaultTtl },
                cancellationToken);

            _logger.LogDebug(
                "Stored idempotency result for {RequestName} key={Key} TTL={TTL}",
                typeof(TRequest).Name, request.IdempotencyKey, DefaultTtl);

            return response;
        }
    }
}
