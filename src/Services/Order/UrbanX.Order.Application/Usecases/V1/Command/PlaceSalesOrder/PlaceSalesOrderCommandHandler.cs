using Shared.Application;
using Shared.Application.Authorization;
using Shared.Contract.Messaging.PlaceOrderSaga;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Services;
using UrbanX.Order.Application.Usecases.V1.Command.Common;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Models;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceSalesOrder;

public sealed class PlaceSalesOrderCommandHandler(
    IEventPublisher eventPublisher,
    IPendingOrderSlotService pendingSlots,
    IFlashSaleStockService flashSaleStock,
    IUserContext userContext)
    : ICommandHandler<PlaceSalesOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceSalesOrderCommand cmd, CancellationToken ct)
    {
        var userId = userContext.UserId ?? Guid.Empty;
        if (userId == Guid.Empty)
            return Result.Failure<Guid>(OrderErrors.Forbidden);

        var slot = await pendingSlots.TryAcquireAsync(userId, OrderType.Sales, ct);
        if (slot.IsFailure)
            return Result.Failure<Guid>(slot.Error);

        var totalQty = cmd.Items.Sum(i => i.Quantity);
        var stockResult = await flashSaleStock.TryReserveAsync(cmd.CampaignId, totalQty, ct);
        if (stockResult.IsFailure)
        {
            await pendingSlots.ReleaseAsync(userId, OrderType.Sales, ct);
            return Result.Failure<Guid>(stockResult.Error);
        }

        var ticketId = Guid.NewGuid();
        var subtotal = PlaceOrderEventMappings.SumLineTotal(cmd.Items);

        try
        {
            await eventPublisher.PublishAsync(new PlaceSalesOrderRequestedV1
            {
                OrderId = ticketId,
                CorrelationId = ticketId.ToString("D"),
                UserId = userId.ToString("D"),
                CampaignId = cmd.CampaignId,
                IdempotencyKey = cmd.IdempotencyKey,
                ExpectedTotal = cmd.ExpectedTotal,
                CouponCode = cmd.CouponCode,
                Subtotal = subtotal,
                ShippingFee = cmd.ShippingFee,
                ShippingAddress = PlaceOrderEventMappings.MapShipping(cmd.ShippingAddress),
                PricingSnapshot = new PricingSnapshot(
                    Subtotal: subtotal,
                    ShippingFee: cmd.ShippingFee,
                    TotalBeforeDiscount: subtotal + cmd.ShippingFee),
                CustomerEmail = cmd.CustomerEmail?.Trim() ?? string.Empty,
                CustomerName = cmd.ShippingAddress.FullName,
                CustomerPhone = cmd.ShippingAddress.Phone,
                CustomerNote = cmd.CustomerNote,
                Items = PlaceOrderEventMappings.MapSalesItems(cmd.Items)
            }, ct);
        }
        catch
        {
            await flashSaleStock.RestoreAsync(cmd.CampaignId, totalQty, ct);
            await pendingSlots.ReleaseAsync(userId, OrderType.Sales, ct);
            throw;
        }

        return Result.Success(ticketId);
    }
}
