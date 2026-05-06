using Shared.Application.Authorization;
using Shared.Cache.Abstractions;
using StackExchange.Redis;

namespace UrbanX.Order.API.Middleware;

public sealed class PlaceOrderRateLimitMiddleware(
    RequestDelegate next,
    ICacheService cacheService,
    IUserContext userContext)
{
    private const int LimitPerMinute = 5;
    private const int WindowMilliseconds = 60_000;

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

        var userId = userContext.UserId;
        if (userId is null || userId == Guid.Empty)
        {
            await next(context);
            return;
        }

        var rateLimitKey = $"rate:order:{userId.Value}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var redisResult = await cacheService.EvalAsync(
            SlidingWindowScript,
            [rateLimitKey],
            [now, WindowMilliseconds, LimitPerMinute],
            context.RequestAborted);

        if (redisResult is not RedisResult[] values || values.Length < 2 || (int)values[0] == 1)
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = "60";
        await Results.Problem(
            detail: "Rate limit exceeded. Please retry after 60 seconds.",
            statusCode: StatusCodes.Status429TooManyRequests,
            type: "ORDER_RATE_LIMITED").ExecuteAsync(context);
    }

    private static bool ShouldApply(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Path.StartsWithSegments("/api/v1/orders", StringComparison.OrdinalIgnoreCase);
}
