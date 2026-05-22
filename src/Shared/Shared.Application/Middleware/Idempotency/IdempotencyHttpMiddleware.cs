using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Cache.DependencyInjection.Options;

namespace Shared.Messaging.Idempotency;

/// <summary>
/// Enforces <c>Idempotency-Key</c> (UUID v4) for matching requests, caches 2xx/4xx responses in Redis for 24h.
/// </summary>
public sealed partial class IdempotencyHttpMiddleware
{
    /// <summary>
    /// Default: <c>/api</c> prefix and non-idempotent HTTP methods only (<c>POST</c>, <c>PUT</c>, <c>PATCH</c>).
    /// </summary>
    private static bool DefaultShouldApply(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api")
        && (HttpMethods.IsPost(context.Request.Method)
            || HttpMethods.IsPut(context.Request.Method)
            || HttpMethods.IsPatch(context.Request.Method));

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [GeneratedRegex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-4[0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex UuidV4Regex();

    private static readonly TimeSpan ResponseTtl = TimeSpan.FromHours(24);

    private readonly RequestDelegate _next;
    private readonly IOptions<IdempotencyHttpOptions> _httpOptions;
    private readonly IOptions<CacheOptions> _cacheOptions;
    private readonly IDistributedCache _cache;
    private readonly ILogger<IdempotencyHttpMiddleware> _logger;

    public IdempotencyHttpMiddleware(
        RequestDelegate next,
        IOptions<IdempotencyHttpOptions> httpOptions,
        IOptions<CacheOptions> cacheOptions,
        IDistributedCache cache,
        ILogger<IdempotencyHttpMiddleware> logger)
    {
        _next = next;
        _httpOptions = httpOptions;
        _cacheOptions = cacheOptions;
        _cache = cache;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var opts = _httpOptions.Value;
        var shouldApply = opts.ShouldApply ?? DefaultShouldApply;

        if (!shouldApply(context))
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrWhiteSpace(opts.ServiceId))
        {
            throw new InvalidOperationException(
                "IdempotencyHttpOptions.ServiceId must be set when HTTP idempotency applies (configure via AddHttpIdempotency).");
        }

        if (!context.Request.Headers.TryGetValue(IdempotencyHttpConstants.IdempotencyKeyHeader, out var rawKey)
            || string.IsNullOrWhiteSpace(rawKey))
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, IdempotencyHttpConstants.MissingKeyType);
            return;
        }

        var keyText = rawKey.ToString().Trim();
        if (!UuidV4Regex().IsMatch(keyText))
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, IdempotencyHttpConstants.InvalidKeyType);
            return;
        }

        var redisKey = BuildRedisKey(keyText, opts.ServiceId);
        var cachedJson = await _cache.GetStringAsync(redisKey, context.RequestAborted);
        if (cachedJson is not null)
        {
            CachedHttpSnapshot? snapshot = null;
            try
            {
                snapshot = JsonSerializer.Deserialize<CachedHttpSnapshot>(cachedJson, SerializerOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Removing unreadable idempotency HTTP cache entry for key {RedisKey}", redisKey);
                await _cache.RemoveAsync(redisKey, context.RequestAborted);
            }

            if (snapshot is not null)
            {
                await ReplayCachedResponseAsync(context, snapshot);
                _logger.LogInformation(
                    "HTTP idempotency cache hit for key {Key} ({RedisKey}).",
                    keyText,
                    redisKey);
                return;
            }
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        var status = context.Response.StatusCode;
        buffer.Position = 0;
        var bodyBytes = buffer.ToArray();

        await originalBody.WriteAsync(bodyBytes, context.RequestAborted);

        if (ShouldCacheStatus(status))
        {
            var snapshot = new CachedHttpSnapshot
            {
                StatusCode = status,
                ContentType = context.Response.ContentType,
                Body = bodyBytes
            };

            try
            {
                await _cache.SetStringAsync(
                    redisKey,
                    JsonSerializer.Serialize(snapshot, SerializerOptions),
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = ResponseTtl
                    },
                    context.RequestAborted);

                _logger.LogInformation(
                    "HTTP idempotency response cached for key {Key} status {Status} ({RedisKey}).",
                    keyText,
                    status,
                    redisKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store HTTP idempotency entry for {RedisKey}", redisKey);
            }
        }
    }

    private string BuildRedisKey(string idempotencyKey, string serviceId)
    {
        var instance = string.IsNullOrWhiteSpace(_cacheOptions.Value.InstanceName)
            ? "urbanx"
            : _cacheOptions.Value.InstanceName.Trim();

        return $"{instance}:idempotency:{idempotencyKey}:{serviceId}";
    }

    private static bool ShouldCacheStatus(int statusCode) =>
        statusCode is >= 200 and <= 499;

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string type)
    {
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new { type }, SerializerOptions, cancellationToken: context.RequestAborted);
    }

    private static async Task ReplayCachedResponseAsync(HttpContext context, CachedHttpSnapshot snapshot)
    {
        context.Response.StatusCode = snapshot.StatusCode;
        if (!string.IsNullOrEmpty(snapshot.ContentType))
        {
            context.Response.ContentType = snapshot.ContentType;
        }

        if (snapshot.Body.Length > 0)
        {
            context.Response.ContentLength = snapshot.Body.Length;
            await context.Response.Body.WriteAsync(snapshot.Body, context.RequestAborted);
        }
    }

    private sealed class CachedHttpSnapshot
    {
        public int StatusCode { get; set; }

        public string? ContentType { get; set; }

        public byte[] Body { get; set; } = Array.Empty<byte>();
    }
}
