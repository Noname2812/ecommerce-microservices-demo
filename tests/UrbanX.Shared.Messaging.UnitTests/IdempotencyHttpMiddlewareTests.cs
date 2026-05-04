using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Shared.Cache.DependencyInjection.Options;
using Shared.Messaging.Idempotency;

namespace UrbanX.Shared.Messaging.UnitTests;

public class IdempotencyHttpMiddlewareTests
{
    private const string ValidV4A = "f47ac10b-58cc-4372-a567-0e02b2c3d479";
    private const string ValidV4B = "a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a14";

    private sealed class HitCounter
    {
        private int _hits;
        public int Hits => _hits;
        public void Increment() => System.Threading.Interlocked.Increment(ref _hits);
    }

    private static async Task<(WebApplication app, HitCounter counter)> CreateHostAsync(
        bool useDefaultShouldApply = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDistributedMemoryCache();
        builder.Services.Configure<CacheOptions>(o => o.InstanceName = "ut");
        var counter = new HitCounter();
        builder.Services.AddHttpIdempotency(o =>
        {
            o.ServiceId = "order";
            if (!useDefaultShouldApply)
            {
                o.ShouldApply = _ => true;
            }
        });
        var app = builder.Build();
        app.UseHttpIdempotency();
        app.MapPost("/api/orders", () =>
        {
            counter.Increment();
            return Results.Json(new { hits = counter.Hits });
        });
        await app.StartAsync();
        return (app, counter);
    }

    /// <summary>Uses built-in ShouldApply (POST/PUT/PATCH under /api only).</summary>
    private static async Task<(WebApplication app, HitCounter counter)> CreateGetHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDistributedMemoryCache();
        builder.Services.Configure<CacheOptions>(o => o.InstanceName = "ut");
        var counter = new HitCounter();
        builder.Services.AddHttpIdempotency(o => o.ServiceId = "order");
        var app = builder.Build();
        app.UseHttpIdempotency();
        app.MapGet("/api/orders", () =>
        {
            counter.Increment();
            return Results.Ok(new { ok = true });
        });
        await app.StartAsync();
        return (app, counter);
    }

    /// <summary>Default ShouldApply; POST always returns 500.</summary>
    private static async Task<(WebApplication app, HitCounter counter)> Create500PostHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDistributedMemoryCache();
        builder.Services.Configure<CacheOptions>(o => o.InstanceName = "ut");
        var counter = new HitCounter();
        builder.Services.AddHttpIdempotency(o => o.ServiceId = "order");
        var app = builder.Build();
        app.UseHttpIdempotency();
        app.MapPost("/api/orders", () =>
        {
            counter.Increment();
            return Results.StatusCode(500);
        });
        await app.StartAsync();
        return (app, counter);
    }

    [Fact]
    public async Task Post_WithoutIdempotencyKey_Returns400WithMissingType()
    {
        // Arrange
        var (app, _) = await CreateHostAsync(useDefaultShouldApply: true);
        await using (app)
        {
            var client = app.GetTestClient();

            // Act
            var res = await client.PostAsync("/api/orders", null);

            // Assert
            Assert.Equal(400, (int)res.StatusCode);
            var doc = await res.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(IdempotencyHttpConstants.MissingKeyType, doc.GetProperty("type").GetString());
        }
    }

    [Fact]
    public async Task Post_InvalidIdempotencyKey_Returns400WithInvalidType()
    {
        // Arrange
        var (app, counter) = await CreateHostAsync();
        await using (app)
        {
            var client = app.GetTestClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
            req.Headers.Add(IdempotencyHttpConstants.IdempotencyKeyHeader, "6ba7b810-9dad-11d1-80b4-00c04fd430c8");

            // Act
            var res = await client.SendAsync(req);

            // Assert
            Assert.Equal(400, (int)res.StatusCode);
            var doc = await res.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(IdempotencyHttpConstants.InvalidKeyType, doc.GetProperty("type").GetString());
            Assert.Equal(0, counter.Hits);
        }
    }

    [Fact]
    public async Task Post_SameIdempotencyKeyTwice_SecondResponseFromCache_HandlerRunsOnce()
    {
        // Arrange
        var (app, counter) = await CreateHostAsync();
        await using (app)
        {
            var client = app.GetTestClient();

            using var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
            req1.Headers.Add(IdempotencyHttpConstants.IdempotencyKeyHeader, ValidV4A);
            using var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
            req2.Headers.Add(IdempotencyHttpConstants.IdempotencyKeyHeader, ValidV4A);

            // Act
            var first = await client.SendAsync(req1);
            var second = await client.SendAsync(req2);

            // Assert
            Assert.Equal(200, (int)first.StatusCode);
            Assert.Equal(200, (int)second.StatusCode);
            Assert.Equal(1, counter.Hits);
            var body1 = await first.Content.ReadFromJsonAsync<JsonElement>();
            var body2 = await second.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(body1.GetRawText(), body2.GetRawText());
        }
    }

    [Fact]
    public async Task Post_DifferentIdempotencyKeys_HandlerRunsTwice()
    {
        // Arrange
        var (app, counter) = await CreateHostAsync();
        await using (app)
        {
            var client = app.GetTestClient();

            using var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
            req1.Headers.Add(IdempotencyHttpConstants.IdempotencyKeyHeader, ValidV4A);
            using var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
            req2.Headers.Add(IdempotencyHttpConstants.IdempotencyKeyHeader, ValidV4B);

            // Act
            await client.SendAsync(req1);
            await client.SendAsync(req2);

            // Assert
            Assert.Equal(2, counter.Hits);
        }
    }

    [Fact]
    public async Task Get_ApiPath_WithoutIdempotencyKey_DoesNotReturn400_HandlerRuns()
    {
        // Arrange
        var (app, counter) = await CreateGetHostAsync();
        await using (app)
        {
            var client = app.GetTestClient();

            // Act
            var res = await client.GetAsync("/api/orders");

            // Assert
            Assert.Equal(200, (int)res.StatusCode);
            Assert.Equal(1, counter.Hits);
        }
    }

    [Fact]
    public async Task Post_5xxResponse_NotCached_HandlerRunsTwiceOnRetry()
    {
        // Arrange
        var (app, counter) = await Create500PostHostAsync();
        await using (app)
        {
            var client = app.GetTestClient();

            using var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
            req1.Headers.Add(IdempotencyHttpConstants.IdempotencyKeyHeader, ValidV4A);
            using var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
            req2.Headers.Add(IdempotencyHttpConstants.IdempotencyKeyHeader, ValidV4A);

            // Act
            var first = await client.SendAsync(req1);
            var second = await client.SendAsync(req2);

            // Assert
            Assert.Equal(500, (int)first.StatusCode);
            Assert.Equal(500, (int)second.StatusCode);
            Assert.Equal(2, counter.Hits);
        }
    }
}
