using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Refit;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Outbox.Abstractions;
using UrbanX.Order.Application.Clients;
using UrbanX.Order.Application.Exceptions;
using UrbanX.Order.Infrastructure.RefitApi.Coupon;
using UrbanX.Order.Infrastructure.Services;
using UrbanX.Services.Order.UnitTests.Infrastructure.Helpers;
using Xunit;

namespace UrbanX.Services.Order.UnitTests.Infrastructure;

public sealed class CouponClientTests
{
    private static RefitSettings CreateRefitSettings() => new()
    {
        ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        })
    };

    private static void VerifyCompensationEnqueueOnce(Mock<ICompensationOutboxWriter> writerMock, Guid reservationId)
    {
        writerMock.Verify(
            w => w.AddAsync(
                It.Is<InventoryReleaseRequestedV1>(e =>
                    e.ReservationId == reservationId &&
                    e.Reason == "COUPON_CLAIM_FAILED"),
                It.Is<CancellationToken>(ct => ct.Equals(CancellationToken.None))),
            Times.Once);
    }

    [Fact]
    public async Task ClaimAsync_WhenCreated_ReturnsClaimCouponResponse()
    {
        var claimId = Guid.Parse("d1e2f3a4-b5c6-d789-e012-f34567890123");
        var expiresAt = DateTimeOffset.Parse("2026-05-07T14:30:00Z");
        var orderIk = "550e8400-e29b-41d4-a716-446655440000";

        var writerMock = new Mock<ICompensationOutboxWriter>();

        var handler = new CallbackHttpMessageHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.NotNull(request.RequestUri);
            Assert.Contains("/internal/v1/coupon-claims", request.RequestUri!.ToString(), StringComparison.Ordinal);

            var json = await request.Content!.ReadAsStringAsync();
            Assert.Contains($"{orderIk}:cpn", json, StringComparison.Ordinal);

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    $$"""
                     {"claimId":"{{claimId}}","discountAmount":12.5,"expiresAt":"2026-05-07T14:30:00.0000000+00:00"}
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

        var api = RestService.For<ICouponApi>(http, CreateRefitSettings());
        var sut = new CouponClient(api, NullLogger<CouponClient>.Instance);
        var reservationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var result = await sut.ClaimAsync(
            new ClaimCouponRequest(orderIk, "SUMMER10", Guid.Parse("11111111-2222-3333-4444-555555555555"), 100m),
            new CouponClaimReservationContext(reservationId, writerMock.Object));

        Assert.Equal(claimId, result.ClaimId);
        Assert.Equal(12.50m, result.DiscountAmount);
        Assert.Equal(expiresAt, result.ExpiresAt);

        writerMock.Verify(
            w => w.AddAsync(It.IsAny<InventoryReleaseRequestedV1>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task ClaimAsync_WhenConflict_ThrowsCouponException_AndWritesCompensation()
    {
        const string payload = """{"type":"COUPON_ALREADY_USED","detail":"duplicate"}""";

        var writerMock = new Mock<ICompensationOutboxWriter>();

        var handler = new CallbackHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
            }));

        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var api = RestService.For<ICouponApi>(http, CreateRefitSettings());
        var sut = new CouponClient(api, NullLogger<CouponClient>.Instance);
        var reservationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var ex = await Assert.ThrowsAsync<CouponException>(() =>
            sut.ClaimAsync(
                new ClaimCouponRequest(
                    "550e8400-e29b-41d4-a716-446655440000",
                    "SUMMER10",
                    Guid.NewGuid(),
                    50m),
                new CouponClaimReservationContext(reservationId, writerMock.Object)));

        Assert.Equal("COUPON_ALREADY_USED", ex.ErrorType);
        Assert.Equal("duplicate", ex.Detail);

        VerifyCompensationEnqueueOnce(writerMock, reservationId);
    }

    [Fact]
    public async Task ClaimAsync_WhenUnprocessableEntity_ThrowsCouponValidationException_AndWritesCompensation()
    {
        const string payload = """{"type":"COUPON_NOT_FOUND","detail":"Coupon does not exist"}""";

        var writerMock = new Mock<ICompensationOutboxWriter>();

        var handler = new CallbackHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
            }));

        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var api = RestService.For<ICouponApi>(http, CreateRefitSettings());
        var sut = new CouponClient(api, NullLogger<CouponClient>.Instance);
        var reservationId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        var ex = await Assert.ThrowsAsync<CouponValidationException>(() =>
            sut.ClaimAsync(
                new ClaimCouponRequest(
                    "550e8400-e29b-41d4-a716-446655440000",
                    "MISSING",
                    Guid.NewGuid(),
                    50m),
                new CouponClaimReservationContext(reservationId, writerMock.Object)));

        Assert.Equal("Coupon does not exist", ex.Message);

        VerifyCompensationEnqueueOnce(writerMock, reservationId);
    }

    [Fact]
    public async Task ClaimAsync_WhenTimeout_WritesCompensation_AndThrowsCouponUnavailableException()
    {
        var writerMock = new Mock<ICompensationOutboxWriter>();

        using var http = new HttpClient(new TimeoutFaultingHandler())
        {
            BaseAddress = new Uri("http://localhost/")
        };

        var api = RestService.For<ICouponApi>(http, CreateRefitSettings());
        var sut = new CouponClient(api, NullLogger<CouponClient>.Instance);
        var reservationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        var ex = await Assert.ThrowsAsync<CouponUnavailableException>(() =>
            sut.ClaimAsync(
                new ClaimCouponRequest(
                    "550e8400-e29b-41d4-a716-446655440000",
                    "SUMMER10",
                    Guid.NewGuid(),
                    50m),
                new CouponClaimReservationContext(reservationId, writerMock.Object)));

        Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);

        VerifyCompensationEnqueueOnce(writerMock, reservationId);
    }
}
