using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using UrbanX.Order.Application.Clients;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Infrastructure.Services;

namespace UrbanX.Services.Order.UnitTests.Infrastructure.Services;

public sealed class CatalogServiceClientTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task GetVariantsAsync_EmptyIds_ReturnsEmptySuccess()
    {
        var sut = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await sut.GetVariantsAsync([], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task GetVariantsAsync_ParsesBatchResponse()
    {
        var variantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var payload = new
        {
            items = new[]
            {
                new
                {
                    productId,
                    productName = "Phone",
                    productIsActive = true,
                    variantId,
                    sku = "SKU-1",
                    variantName = "128GB",
                    variantIsActive = true,
                    currentPrice = 999.99m,
                    sellerId,
                    sellerName = "Urban Seller",
                    sellerIsActive = true,
                    imageUrl = "https://cdn/img.png"
                }
            }
        };

        HttpRequestMessage? captured = null;
        var sut = CreateSut(req =>
        {
            captured = req;
            return JsonResponse(payload);
        });

        var result = await sut.GetVariantsAsync([variantId], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        var item = result.Value[0];
        Assert.Equal(productId, item.ProductId);
        Assert.Equal("Phone", item.ProductName);
        Assert.True(item.ProductIsActive);
        Assert.Equal(variantId, item.VariantId);
        Assert.Equal("SKU-1", item.Sku);
        Assert.Equal("128GB", item.VariantName);
        Assert.True(item.VariantIsActive);
        Assert.Equal(999.99m, item.CurrentPrice);
        Assert.Equal(sellerId, item.SellerId);
        Assert.Equal("Urban Seller", item.SellerName);
        Assert.True(item.SellerIsActive);
        Assert.Equal("https://cdn/img.png", item.ImageUrl);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Contains($"/api/v1/catalog/variants/batch?ids={variantId:D}", captured.RequestUri?.ToString());
    }

    [Fact]
    public async Task GetVariantsAsync_NotFound_ReturnsVariantNotFound()
    {
        var sut = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await sut.GetVariantsAsync([Guid.NewGuid()], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Order.CatalogValidationFailed", result.Error.Code);
        Assert.Equal("VARIANT_NOT_FOUND", result.Error.Message);
    }

    [Fact]
    public async Task GetVariantsAsync_PartialPayload_ReturnsVariantNotFound()
    {
        var requested = Guid.NewGuid();
        var payload = new { items = Array.Empty<object>() };
        var sut = CreateSut(_ => JsonResponse(payload));

        var result = await sut.GetVariantsAsync([requested], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VARIANT_NOT_FOUND", result.Error.Message);
    }

    [Fact]
    public async Task GetVariantsAsync_ServiceUnavailable_ReturnsCatalogUnavailable()
    {
        var sut = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await sut.GetVariantsAsync([Guid.NewGuid()], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.CatalogUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task GetVariantsAsync_NetworkError_ReturnsCatalogUnavailable()
    {
        var sut = CreateSut(_ => throw new HttpRequestException("connection reset"));

        var result = await sut.GetVariantsAsync([Guid.NewGuid()], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.CatalogUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task ValidateProductsAsync_ParsesResponse()
    {
        var productId = Guid.NewGuid();
        var payload = new
        {
            items = new[]
            {
                new { productId, exists = true, isActive = true }
            }
        };

        HttpRequestMessage? captured = null;
        var sut = CreateSut(req =>
        {
            captured = req;
            return JsonResponse(payload);
        });

        var result = await sut.ValidateProductsAsync([productId], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.ContainsKey(productId));
        Assert.True(result.Value[productId].Exists);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Contains("/api/v1/catalog/internal/validate-products", captured.RequestUri?.ToString());
    }

    [Fact]
    public async Task ValidateProductsAsync_InternalServerError_ReturnsCatalogUnavailable()
    {
        var sut = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await sut.ValidateProductsAsync([Guid.NewGuid()], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.CatalogUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task ValidateProductsAsync_ServiceUnavailable_ReturnsCatalogUnavailable()
    {
        var sut = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await sut.ValidateProductsAsync([Guid.NewGuid()], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.CatalogUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task ValidateProductsAsync_NetworkError_ReturnsCatalogUnavailable()
    {
        var sut = CreateSut(_ => throw new HttpRequestException("connection reset"));

        var result = await sut.ValidateProductsAsync([Guid.NewGuid()], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.CatalogUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task GetCurrentPricesAsync_ParsesResponse()
    {
        var variantId = Guid.NewGuid();
        var payload = new
        {
            items = new[]
            {
                new { variantId, currentPrice = 42.50m }
            }
        };

        HttpRequestMessage? captured = null;
        var sut = CreateSut(req =>
        {
            captured = req;
            return JsonResponse(payload);
        });

        var result = await sut.GetCurrentPricesAsync([variantId], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(42.50m, result.Value[variantId].CurrentPrice);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Contains("/api/v1/catalog/internal/variant-prices", captured.RequestUri?.ToString());
    }

    [Fact]
    public async Task GetCurrentPricesAsync_InternalServerError_ReturnsCatalogUnavailable()
    {
        var sut = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await sut.GetCurrentPricesAsync([Guid.NewGuid()], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.CatalogUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task GetCurrentPricesAsync_ServiceUnavailable_ReturnsCatalogUnavailable()
    {
        var sut = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await sut.GetCurrentPricesAsync([Guid.NewGuid()], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.CatalogUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task GetCurrentPricesAsync_NetworkError_ReturnsCatalogUnavailable()
    {
        var sut = CreateSut(_ => throw new HttpRequestException("connection reset"));

        var result = await sut.GetCurrentPricesAsync([Guid.NewGuid()], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.CatalogUnavailable.Code, result.Error.Code);
    }

    private static CatalogServiceClient CreateSut(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHttpHandler(responder);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://catalog.test") };
        return new CatalogServiceClient(client, NullLogger<CatalogServiceClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(object payload) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOpts),
                Encoding.UTF8,
                "application/json")
        };

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
