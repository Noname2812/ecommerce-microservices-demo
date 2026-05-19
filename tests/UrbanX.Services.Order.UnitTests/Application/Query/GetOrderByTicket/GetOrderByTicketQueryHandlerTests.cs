using Microsoft.Extensions.Options;
using Moq;
using Shared.Application.Authorization;
using Shared.Cache.Abstractions;
using UrbanX.Order.Application.Abstractions;
using UrbanX.Order.Application.DependencyInjection.Options;
using UrbanX.Order.Application.Usecases.V1.Query.GetOrderByTicket;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;
using UrbanX.Order.Domain.ValueObjects;
using OrderEntity = UrbanX.Order.Domain.Models.Order;

namespace UrbanX.Services.Order.UnitTests.Application.Query.GetOrderByTicket;

public sealed class GetOrderByTicketQueryHandlerTests
{
    private readonly Mock<IOrderRepository> _orderRepository = new();
    private readonly Mock<IOrderTicketStatusQuery> _ticketStatusQuery = new();
    private readonly Mock<IUserContext> _userContext = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly OrderTicketCacheOptions _cacheOptions = new();

    private readonly Guid _ownerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _otherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public GetOrderByTicketQueryHandlerTests()
    {
        _cache
            .Setup(c => c.GetAsync<OrderTicketStatusDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderTicketStatusDto?)null);
        _cache
            .Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<OrderTicketStatusDto>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private GetOrderByTicketQueryHandler CreateSut() =>
        new(
            _orderRepository.Object,
            _ticketStatusQuery.Object,
            _userContext.Object,
            _cache.Object,
            Options.Create(_cacheOptions));

    [Fact]
    public async Task Handle_WhenCacheHit_DoesNotQueryDatabase()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var cached = new OrderTicketStatusDto(
            ticketId,
            OrderStatus.Confirmed,
            ticketId,
            null,
            null,
            PaymentStatus.Paid,
            null,
            null);

        _cache
            .Setup(c => c.GetAsync<OrderTicketStatusDto>(
                CacheKey(ticketId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        // Act
        var result = await CreateSut().Handle(new GetOrderByTicketQuery(ticketId), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(cached, result.Value);
        _orderRepository.Verify(
            r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _ticketStatusQuery.Verify(
            q => q.GetSagaByTicketIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _cache.Verify(
            c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<OrderTicketStatusDto>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCacheMissAndPendingPayment_SetsNonTerminalTtl()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var order = CreateOrder(ticketId, _ownerId);
        order.MarkReadyForPayment(
            Guid.NewGuid(), null,
            "https://pay.example/checkout",
            "https://pay.example/qr",
            _ownerId, "Owner");

        _userContext.Setup(x => x.UserId).Returns(_ownerId);
        _userContext.Setup(x => x.HasRole(Roles.Admin)).Returns(false);
        _orderRepository
            .Setup(r => r.GetByIdAsync(ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _ticketStatusQuery
            .Setup(q => q.GetSagaByTicketIdAsync(ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderTicketSagaSnapshot?)null);

        TimeSpan? capturedTtl = null;
        _cache
            .Setup(c => c.SetAsync(
                CacheKey(ticketId),
                It.IsAny<OrderTicketStatusDto>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, OrderTicketStatusDto, TimeSpan?, CancellationToken>((_, _, ttl, _) =>
                capturedTtl = ttl)
            .Returns(Task.CompletedTask);

        // Act
        await CreateSut().Handle(new GetOrderByTicketQuery(ticketId), CancellationToken.None);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(_cacheOptions.NonTerminalTtlSeconds), capturedTtl);
    }

    [Fact]
    public async Task Handle_WhenCacheMissAndConfirmed_SetsTerminalTtl()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var order = CreateOrder(ticketId, _ownerId);
        order.MarkReadyForPayment(
            Guid.NewGuid(), null,
            "https://pay.example/checkout",
            "https://pay.example/qr",
            _ownerId, "Owner");
        order.MarkPaid("pay-session-1", _ownerId, "Owner");

        _userContext.Setup(x => x.UserId).Returns(_ownerId);
        _userContext.Setup(x => x.HasRole(Roles.Admin)).Returns(false);
        _orderRepository
            .Setup(r => r.GetByIdAsync(ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _ticketStatusQuery
            .Setup(q => q.GetSagaByTicketIdAsync(ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderTicketSagaSnapshot?)null);

        TimeSpan? capturedTtl = null;
        _cache
            .Setup(c => c.SetAsync(
                CacheKey(ticketId),
                It.IsAny<OrderTicketStatusDto>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, OrderTicketStatusDto, TimeSpan?, CancellationToken>((_, _, ttl, _) =>
                capturedTtl = ttl)
            .Returns(Task.CompletedTask);

        // Act
        await CreateSut().Handle(new GetOrderByTicketQuery(ticketId), CancellationToken.None);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(_cacheOptions.TerminalTtlSeconds), capturedTtl);
    }

    [Fact]
    public async Task Handle_WhenTicketNotFound_DoesNotWriteCache()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        _orderRepository
            .Setup(r => r.GetByIdAsync(ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderEntity?)null);
        _ticketStatusQuery
            .Setup(q => q.GetSagaByTicketIdAsync(ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderTicketSagaSnapshot?)null);

        // Act
        await CreateSut().Handle(new GetOrderByTicketQuery(ticketId), CancellationToken.None);

        // Assert
        _cache.Verify(
            c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<OrderTicketStatusDto>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenOrderExists_ReturnsOrderStatusAndPaymentUrl()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var order = CreateOrder(ticketId, _ownerId);
        order.MarkReadyForPayment(
            Guid.NewGuid(), null,
            "https://pay.example/checkout",
            "https://pay.example/qr",
            _ownerId, "Owner");

        _userContext.Setup(x => x.UserId).Returns(_ownerId);
        _userContext.Setup(x => x.HasRole(Roles.Admin)).Returns(false);
        _orderRepository
            .Setup(r => r.GetByIdAsync(ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _ticketStatusQuery
            .Setup(q => q.GetSagaByTicketIdAsync(ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderTicketSagaSnapshot("AwaitingPayment", null, null,
                DateTimeOffset.UtcNow.AddMinutes(15)));

        // Act
        var result = await CreateSut().Handle(new GetOrderByTicketQuery(ticketId), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.PendingPayment, result.Value.Status);
        Assert.Equal(ticketId, result.Value.OrderId);
        Assert.Equal("https://pay.example/checkout", result.Value.PaymentUrl);
        Assert.Equal("https://pay.example/qr", result.Value.QrCodeUrl);
        Assert.NotNull(result.Value.PaymentExpiresAt);
    }

    [Fact]
    public async Task Handle_WhenOrderMissingAndNormalSagaActive_ReturnsProcessing()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        _orderRepository
            .Setup(r => r.GetByIdAsync(ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderEntity?)null);
        _ticketStatusQuery
            .Setup(q => q.GetSagaByTicketIdAsync(ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderTicketSagaSnapshot("ValidatingCatalog", null, null, null));

        // Act
        var result = await CreateSut().Handle(new GetOrderByTicketQuery(ticketId), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("PROCESSING", result.Value.Status);
        Assert.Equal(ticketId, result.Value.OrderId);
        Assert.Null(result.Value.PaymentUrl);
        Assert.Null(result.Value.CancelledReason);
    }

    [Fact]
    public async Task Handle_WhenOrderMissingAndNormalSagaFaulted_ReturnsCancelledWithReason()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        _orderRepository
            .Setup(r => r.GetByIdAsync(ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderEntity?)null);
        _ticketStatusQuery
            .Setup(q => q.GetSagaByTicketIdAsync(ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderTicketSagaSnapshot(
                "Faulted", "Inventory reserve failed", "Variant out of stock", null));

        // Act
        var result = await CreateSut().Handle(new GetOrderByTicketQuery(ticketId), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("CANCELLED", result.Value.Status);
        Assert.Null(result.Value.OrderId);
        Assert.Equal("Variant out of stock", result.Value.CancelledReason);
    }

    [Fact]
    public async Task Handle_WhenOrderMissingAndSalesSagaActive_ReturnsProcessing()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        _orderRepository
            .Setup(r => r.GetByIdAsync(ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderEntity?)null);
        _ticketStatusQuery
            .Setup(q => q.GetSagaByTicketIdAsync(ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderTicketSagaSnapshot("ReservingInventory", null, null, null));

        // Act
        var result = await CreateSut().Handle(new GetOrderByTicketQuery(ticketId), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("PROCESSING", result.Value.Status);
        Assert.Equal(ticketId, result.Value.OrderId);
    }

    [Fact]
    public async Task Handle_WhenOrderAndSagaMissing_ReturnsTicketNotFound()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        _orderRepository
            .Setup(r => r.GetByIdAsync(ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderEntity?)null);
        _ticketStatusQuery
            .Setup(q => q.GetSagaByTicketIdAsync(ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderTicketSagaSnapshot?)null);

        // Act
        var result = await CreateSut().Handle(new GetOrderByTicketQuery(ticketId), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.TicketNotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenOrderOwnedByAnotherUserAndNotAdmin_ReturnsForbidden()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var order = CreateOrder(ticketId, _ownerId);
        _userContext.Setup(x => x.UserId).Returns(_otherUserId);
        _userContext.Setup(x => x.HasRole(Roles.Admin)).Returns(false);
        _orderRepository
            .Setup(r => r.GetByIdAsync(ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        // Act
        var result = await CreateSut().Handle(new GetOrderByTicketQuery(ticketId), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.Forbidden.Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenOrderOwnedByAnotherUserButAdmin_ReturnsSuccess()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var order = CreateOrder(ticketId, _ownerId);
        _userContext.Setup(x => x.UserId).Returns(_otherUserId);
        _userContext.Setup(x => x.HasRole(Roles.Admin)).Returns(true);
        _orderRepository
            .Setup(r => r.GetByIdAsync(ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _ticketStatusQuery
            .Setup(q => q.GetSagaByTicketIdAsync(ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderTicketSagaSnapshot?)null);

        // Act
        var result = await CreateSut().Handle(new GetOrderByTicketQuery(ticketId), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Processing, result.Value.Status);
    }

    private static OrderEntity CreateOrder(Guid orderId, Guid userId)
    {
        var items = new[]
        {
            new NewOrderItemSpec(
                Guid.NewGuid(), "Product", "product-slug",
                Guid.NewGuid(), "SKU-001", "Variant",
                Guid.NewGuid(), "Seller",
                50m, 1, 0m, null)
        };

        var address = ShippingAddress.Create(
            "123 Street", null, "District 1", "Ho Chi Minh", null,
            "VN", null, "Recipient", "0900000000");

        return OrderEntity.Create(
            orderId,
            "ORD-TEST-001",
            userId,
            "user@example.com",
            "Test User",
            "0900000000",
            address,
            shippingFee: 0m,
            couponCode: null,
            couponDiscount: 0m,
            saleDiscount: 0m,
            originalPrice: 50m,
            customerNote: null,
            idempotencyKey: "idem-key-1",
            items: items);
    }

    private static string CacheKey(Guid ticketId) => $"order:ticket:{ticketId}";
}
