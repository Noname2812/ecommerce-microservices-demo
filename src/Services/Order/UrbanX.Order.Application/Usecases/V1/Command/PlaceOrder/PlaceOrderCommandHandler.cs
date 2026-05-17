using Shared.Application;
using Shared.Application.Authorization;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;
using UrbanX.Order.Domain.ValueObjects;
using OrderEntity = UrbanX.Order.Domain.Models.Order;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;

public sealed class PlaceOrderCommandHandler(
    IOrderRepository orderRepository,
    IOutboxWriter outboxWriter,
    IUserContext userContext,
    IProductValidator productValidator,
    IShippingValidator shippingValidator,
    IPricingValidator pricingValidator)
    : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        var currentUserId = userContext.UserId;
        if (currentUserId is null || currentUserId == Guid.Empty)
            return Result.Failure<Guid>(OrderErrors.Forbidden);

        var userId = currentUserId.Value;

        var validationResult = await ValidateBusinessRulesAsync(cmd, ct);
        if (validationResult.IsFailure)
            return Result.Failure<Guid>(validationResult.Error);

        var address = ShippingAddress.Create(
            cmd.ShippingAddress.Address, cmd.ShippingAddress.Ward, cmd.ShippingAddress.District,
            cmd.ShippingAddress.City, cmd.ShippingAddress.Province, cmd.ShippingAddress.Country,
            cmd.ShippingAddress.ZipCode, cmd.ShippingAddress.FullName, cmd.ShippingAddress.Phone);

        var specs = cmd.Items.Select(i => new NewOrderItemSpec(
            i.ProductId, i.ProductName, i.ProductSlug,
            i.VariantId, i.VariantSku, i.VariantName,
            i.SellerId, i.SellerName,
            i.UnitPrice, i.Quantity,
            i.DiscountAmount,
            i.ImageUrl
        )).ToList();

        var order = OrderEntity.Create(
            GenerateOrderNumber(),
            userId,
            customerEmail: cmd.CustomerEmail?.Trim() ?? string.Empty,
            customerName: cmd.ShippingAddress.FullName,
            customerPhone: cmd.ShippingAddress.Phone,
            address,
            cmd.ShippingFee,
            cmd.CouponCode,
            couponDiscount: 0m,
            cmd.CustomerNote,
            cmd.IdempotencyKey,
            specs);

        orderRepository.Add(order);

        await outboxWriter.WriteAsync(new PlaceOrderRequestedV1
        {
            OrderId        = order.Id,
            UserId         = userId.ToString("D"),
            IdempotencyKey = cmd.IdempotencyKey,
            CouponCode     = cmd.CouponCode,
            Subtotal       = order.Subtotal,
            ShippingFee    = order.ShippingFee,
            Items          = order.Items
                .Select(i => new NormalOrderItemSnapshot(i.ProductId, i.VariantId, i.Quantity, i.UnitPrice))
                .ToList()
        }, ct);

        return Result.Success(order.Id);
    }

    private async Task<Result> ValidateBusinessRulesAsync(PlaceOrderCommand cmd, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var running = new List<Task<Result>>
        {
            productValidator.ValidateAsync(cmd.Items, cts.Token),
            shippingValidator.ValidateAsync(cmd.ShippingAddress, cts.Token),
            pricingValidator.ValidateAsync(cmd.PricingSnapshot, cmd.Items, cts.Token)
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
                    _ => { },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
                return result;
            }
        }

        return Result.Success();
    }

    private static string GenerateOrderNumber()
    {
        var date = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return $"ORD-{date}-{suffix}";
    }
}
