using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Cache.Abstractions;
using Shared.Contract.Dtos.Order;
using Shared.Contract.Messaging.PlaceOrderSaga;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;
using UrbanX.Order.Application.Usecases.V1.Command.Common;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceSalesOrder;

public sealed class PlaceSalesOrderCommandHandler(
    IOrderRepository orderRepository,
    IOutboxWriter outboxWriter,
    IUserContext userContext,
    IShippingValidator shippingValidator,
    ICacheService cache,
    ILogger<PlaceSalesOrderCommandHandler> logger)
    : ICommandHandler<PlaceSalesOrderCommand, Guid>
{
    private static readonly TimeSpan IdempotencyGuardTtl = TimeSpan.FromHours(24);

    public async Task<Result<Guid>> Handle(PlaceSalesOrderCommand request, CancellationToken ct)
    {
        var currentUserId = userContext.UserId;
        if (currentUserId is null || currentUserId == Guid.Empty)
            return Result.Failure<Guid>(OrderErrors.Forbidden);

        var userId = currentUserId.Value;

        var guardKey = PlaceSalesOrderIdempotencyCacheKeys.GuardKey(request.IdempotencyKey);
        try
        {
            var cachedId = await cache.GetAsync<string>(guardKey, ct);
            if (!string.IsNullOrEmpty(cachedId) &&
                Guid.TryParse(cachedId, out var existingOrderId) &&
                existingOrderId != Guid.Empty)
                return Result.Success(existingOrderId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Idempotency guard cache unavailable for key {Key}", guardKey);
            return Result.Failure<Guid>(OrderErrors.GuardUnavailable);
        }

        // TODO(TASK-06): product/sale eligibility/pricing/quota validation moves to saga + HTTP clients
        var validation = await shippingValidator.ValidateAsync(request.ShippingAddress, ct);
        if (validation.IsFailure)
            return Result.Failure<Guid>(validation.Error);

        var order = OrderFactory.Build(
            request,
            userId,
            OrderNumberGenerator.Generate("SALE"),
            orderType: OrderType.Sales,
            campaignId: request.CampaignId,
            useItemDiscount: false);

        orderRepository.Add(order);

        await outboxWriter.WriteAsync(new PlaceSalesOrderRequestedV1
        {
            CorrelationId  = order.Id.ToString("D"),
            OrderId        = order.Id,
            UserId         = userId.ToString("D"),
            CampaignId     = request.CampaignId,
            IdempotencyKey = request.IdempotencyKey,
            Subtotal       = order.Subtotal,
            ShippingFee    = order.ShippingFee,
            ShippingAddress = new OrderDtos.ShippingAddressSnapshot(
                FullName: order.ShippingAddress.RecipientName,
                Phone:    order.ShippingAddress.RecipientPhone,
                Address:  order.ShippingAddress.Street,
                Ward:     order.ShippingAddress.Ward ?? string.Empty,
                District: order.ShippingAddress.District,
                City:     order.ShippingAddress.City,
                Province: order.ShippingAddress.Province ?? string.Empty,
                Country:  order.ShippingAddress.Country,
                ZipCode:  order.ShippingAddress.ZipCode),
            CouponCode     = request.CouponCode,
            Items          = order.Items
                .Select(i => new OrderItemSnapshot(i.ProductId, i.VariantId, i.Quantity, i.UnitPrice))
                .ToList(),
            PricingSnapshot = new PricingSnapshot(
                Subtotal:            order.Subtotal,
                ShippingFee:         order.ShippingFee,
                TotalBeforeDiscount: order.Subtotal + order.ShippingFee),
            CustomerEmail  = request.CustomerEmail,
            CustomerNote   = request.CustomerNote
        }, ct);

        try
        {
            await cache.SetAsync(guardKey, order.Id.ToString("D"), IdempotencyGuardTtl, CancellationToken.None);
        }
        catch
        {
            // Best-effort; MediatR idempotency may still persist after commit.
        }

        return Result.Success(order.Id);
    }
}
