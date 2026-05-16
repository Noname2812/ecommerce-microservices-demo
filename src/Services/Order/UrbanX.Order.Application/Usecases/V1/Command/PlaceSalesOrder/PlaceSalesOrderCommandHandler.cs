using Shared.Application;
using Shared.Application.Authorization;
using Shared.Cache.Abstractions;
using Shared.Contract.Messaging.PlaceOrderSaga;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;
using UrbanX.Order.Application.Abstractions;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;
using UrbanX.Order.Domain.ValueObjects;
using OrderEntity = UrbanX.Order.Domain.Models.Order;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceSalesOrder;

public sealed class PlaceSalesOrderCommandHandler(
    IOrderRepository orderRepository,
    IOutboxWriter outboxWriter,
    IUserContext userContext,
    IProductValidator productValidator,
    IShippingValidator shippingValidator,
    ISaleEligibilityValidator eligibilityValidator,
    ISaleAllocationGate allocationGate,
    ISalePricingValidator salePricingValidator,
    ICacheService cache,
    PlaceSalesOrderCompensationContext salesCompensationContext)
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
        catch
        {
            // Fail-open: Redis unavailable — continue (pipeline idempotency may still apply).
        }

        var eligibility = await eligibilityValidator.ValidateAsync(request.CampaignId, userId, request.Items, ct);
        if (eligibility.IsFailure)
            return Result.Failure<Guid>(eligibility.Error);

        var totalQty = request.Items.Sum(i => i.Quantity);
        var quotaResult = await allocationGate.TryReserveAsync(request.CampaignId, userId, totalQty, ct);
        if (quotaResult.IsFailure)
            return Result.Failure<Guid>(quotaResult.Error);

        salesCompensationContext.SaleQuotaKey    = quotaResult.Value;
        salesCompensationContext.SaleCampaignId  = request.CampaignId;
        salesCompensationContext.SaleUserId      = userId;
        salesCompensationContext.SaleReservedQty = totalQty;

        var validationResult = await ValidateBusinessRulesAsync(
            request, productValidator, shippingValidator, salePricingValidator, ct);
        if (validationResult.IsFailure)
            return Result.Failure<Guid>(validationResult.Error);

        var orderNumber = GenerateOrderNumber();
        var address = ShippingAddress.Create(
            request.ShippingAddress.Address, request.ShippingAddress.Ward,
            request.ShippingAddress.District, request.ShippingAddress.City,
            request.ShippingAddress.Province, request.ShippingAddress.Country,
            request.ShippingAddress.ZipCode, request.ShippingAddress.FullName,
            request.ShippingAddress.Phone);

        var specs = request.Items.Select(i => new NewOrderItemSpec(
            i.ProductId, i.ProductName, i.ProductSlug,
            i.VariantId, i.VariantSku, i.VariantName,
            i.SellerId, i.SellerName,
            i.UnitPrice, i.Quantity, DiscountAmount: 0m,
            i.ImageUrl)).ToList();

        var order = OrderEntity.Create(
            orderNumber, userId,
            customerEmail: request.CustomerEmail?.Trim() ?? string.Empty,
            customerName: request.ShippingAddress.FullName,
            customerPhone: request.ShippingAddress.Phone,
            address, request.ShippingFee,
            couponCode: request.CouponCode, couponDiscount: 0m,
            request.CustomerNote, request.IdempotencyKey, specs,
            orderType: OrderType.Sales,
            campaignId: request.CampaignId);

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
            ShippingAddress = new ShippingAddressSnapshot(
                RecipientName: order.ShippingAddress.RecipientName,
                PhoneNumber:   order.ShippingAddress.RecipientPhone,
                AddressLine:   order.ShippingAddress.Street,
                Ward:          order.ShippingAddress.Ward ?? string.Empty,
                District:      order.ShippingAddress.District,
                Province:      order.ShippingAddress.Province ?? string.Empty,
                CountryCode:   order.ShippingAddress.Country),
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

    private static async Task<Result> ValidateBusinessRulesAsync(
        PlaceSalesOrderCommand request,
        IProductValidator productValidator,
        IShippingValidator shippingValidator,
        ISalePricingValidator salePricingValidator,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var running = new List<Task<Result>>
        {
            productValidator.ValidateAsync(request.Items, cts.Token),
            shippingValidator.ValidateAsync(request.ShippingAddress, cts.Token),
            salePricingValidator.ValidateAsync(request.CampaignId, request.PricingSnapshot, request.Items, cts.Token)
        };

        while (running.Count > 0)
        {
            var completed = await Task.WhenAny(running);
            running.Remove(completed);
            var result = await completed;
            if (result.IsFailure)
            {
                cts.Cancel();
                _ = Task.WhenAll(running).ContinueWith(
                    _ => { }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                return result;
            }
        }
        return Result.Success();
    }

    private static string GenerateOrderNumber()
    {
        var date   = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return $"SALE-{date}-{suffix}";
    }
}
