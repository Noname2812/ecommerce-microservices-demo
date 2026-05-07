using System.Net;
using System.Text.Json;
using Refit;
using UrbanX.Order.Infrastructure.Exceptions;
using UrbanX.Order.Infrastructure.Services;
using UrbanX.Services.Order.UnitTests.Infrastructure.Helpers;
using Xunit;

namespace UrbanX.Services.Order.UnitTests.Infrastructure;

public sealed class InventoryClientTests
{
    private static RefitSettings CreateRefitSettings() => new()
    {
        ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        })
    };

    [Fact]
    public async Task ReserveAsync_WhenCreated_ReturnsReserveResponse()
    {
        var reservationId = Guid.Parse("a1b2c3d4-e5f6-4789-a012-345678901234");
        var productId = Guid.Parse("b2c3d4e5-f6a7-4890-b123-456789012345");
        var expiresAt = DateTimeOffset.Parse("2026-05-07T12:00:00Z");

        var handler = new CallbackHttpMessageHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.NotNull(request.RequestUri);
            Assert.Contains("/internal/v1/reservations", request.RequestUri!.ToString(), StringComparison.Ordinal);

            var json = await request.Content!.ReadAsStringAsync();
            Assert.Contains("550e8400-e29b-41d4-a716-446655440000:inv", json, StringComparison.Ordinal);

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    $$"""
                    {"reservationId":"{{reservationId}}","expiresAt":"2026-05-07T12:00:00.0000000+00:00","items":[{"productId":"{{productId}}","quantity":2}]}
                    """,
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });

        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost"),
            Timeout = TimeSpan.FromSeconds(30)
        };

        var api = RestService.For<IInventoryApi>(http, CreateRefitSettings());
        var sut = new InventoryClient(api);

        var result = await sut.ReserveAsync(new ReserveRequest(
            "550e8400-e29b-41d4-a716-446655440000",
            [new ReserveLineItem(productId, 2)]));

        Assert.Equal(reservationId, result.ReservationId);
        Assert.Equal(expiresAt, result.ExpiresAt);
        Assert.Single(result.Items);
        Assert.Equal(productId, result.Items[0].ProductId);
        Assert.Equal(2, result.Items[0].Quantity);
    }

    [Fact]
    public async Task ReserveAsync_WhenConflict_ThrowsOutOfStockException_WithDetail()
    {
        const string payload = """{"type":"OUT_OF_STOCK","productId":"...","requested":5,"available":1}""";

        var handler = new CallbackHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
            }));

        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var api = RestService.For<IInventoryApi>(http, CreateRefitSettings());
        var sut = new InventoryClient(api);

        var ex = await Assert.ThrowsAsync<OutOfStockException>(() =>
            sut.ReserveAsync(new ReserveRequest(
                "550e8400-e29b-41d4-a716-446655440000",
                [new ReserveLineItem(Guid.NewGuid(), 5)])));

        Assert.Equal(payload, ex.Detail);
    }

    [Fact]
    public async Task ReserveAsync_WhenTimeout_ThrowsInventoryUnavailableException()
    {
        using var http = new HttpClient(new TimeoutFaultingHandler())
        {
            BaseAddress = new Uri("http://localhost/")
        };

        var api = RestService.For<IInventoryApi>(http, CreateRefitSettings());
        var sut = new InventoryClient(api);

        var ex = await Assert.ThrowsAsync<InventoryUnavailableException>(() =>
            sut.ReserveAsync(new ReserveRequest(
                "550e8400-e29b-41d4-a716-446655440000",
                [new ReserveLineItem(Guid.NewGuid(), 1)])));

        Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
