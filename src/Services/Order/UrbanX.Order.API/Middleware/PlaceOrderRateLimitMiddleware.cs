using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Application.Authorization;
using Shared.Cache.Abstractions;
using StackExchange.Redis;

namespace UrbanX.Order.API.Middleware;

public sealed class PlaceOrderRateLimitMiddleware(
    RequestDelegate next,
    ICacheService cacheService,
    ILogger<PlaceOrderRateLimitMiddleware> logger)
{
    private const int LimitPerMinute = 5;
    private const int WindowMilliseconds = 60_000;
    private const int DegradedConcurrencyLimit = 20;
    private const int DegradedWaitMilliseconds = 200;

    // Process-wide concurrency cap used when Redis is unavailable. Replaces the
    // Redis sliding-window limiter to keep traffic flowing while protecting the
    // DB from a flood. Per-instance — a multi-replica deployment caps to
    // (replicas × DegradedConcurrencyLimit) which is acceptable for degraded mode.
    private static readonly SemaphoreSlim DegradedGate =
        new(initialCount: DegradedConcurrencyLimit, maxCount: DegradedConcurrencyLimit);

    private const string SlidingWindowScript = """
        local key = KEYS[1]
        local now = tonumber(ARGV[1])
        local windowMs = tonumber(ARGV[2])
        local limit = tonumber(ARGV[3])

        redis.call('ZREMRANGEBYSCORE', key, 0, now - windowMs)
        local count = redis.call('ZCARD', key)

        if count >= limit then
            return {0, 60}
        end

        redis.call('ZADD', key, now, tostring(now))
        redis.call('PEXPIRE', key, windowMs)
        return {1, 60}
        """;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldApply(context))
        {
            await next(context);
            return;
        }

        var userContext = context.RequestServices.GetRequiredService<IUserContext>();
        var userId = userContext.UserId;
        if (userId is null || userId == Guid.Empty)
        {
            await next(context);
            return;
        }

        var rateLimitKey = $"rate:order:{userId.Value}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        RedisResult redisResult;
        try
        {
            redisResult = await cacheService.EvalAsync(
                SlidingWindowScript,
                [rateLimitKey],
                [now, WindowMilliseconds, LimitPerMinute],
                context.RequestAborted);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            await InvokeDegradedAsync(context, ex);
            return;
        }

        if (redisResult.IsNull)
        {
            await next(context);
            return;
        }

        var values = (RedisResult[])redisResult;

        if (values.Length < 2 || (int)values[0] == 1)
        {
            await next(context);
            return;
        }

        await WriteRateLimitedAsync(context, "ORDER_RATE_LIMITED",
            "Rate limit exceeded. Please retry after 60 seconds.", retryAfter: "60");
    }

    private async Task InvokeDegradedAsync(HttpContext context, Exception cause)
    {
        logger.LogWarning(cause,
            "[RateLimit] Redis unavailable — entering degraded mode (concurrency cap {Limit}).",
            DegradedConcurrencyLimit);

        bool entered;
        try
        {
            entered = await DegradedGate.WaitAsync(
                TimeSpan.FromMilliseconds(DegradedWaitMilliseconds),
                context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return;
        }

        if (!entered)
        {
            await WriteRateLimitedAsync(context, "SERVICE_DEGRADED",
                "Service is under heavy load. Please retry shortly.", retryAfter: "5");
            return;
        }

        try
        {
            await next(context);
        }
        finally
        {
            DegradedGate.Release();
        }
    }

    private static Task WriteRateLimitedAsync(
        HttpContext context, string problemType, string detail, string retryAfter)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = retryAfter;
        return Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status429TooManyRequests,
            type: problemType).ExecuteAsync(context);
    }

    private static bool IsRedisFailure(Exception ex) =>
        ex is RedisException or RedisTimeoutException or RedisConnectionException or TimeoutException;

    private static bool ShouldApply(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Path.StartsWithSegments("/api/v1/orders", StringComparison.OrdinalIgnoreCase);
}
