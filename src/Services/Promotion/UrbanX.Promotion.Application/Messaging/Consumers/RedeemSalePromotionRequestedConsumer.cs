using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.PlaceOrderSaga;
using Shared.Messaging;
using UrbanX.Promotion.Application.Usecases.V1.Command;

namespace UrbanX.Promotion.Application.Messaging.Consumers;

public sealed class RedeemSalePromotionRequestedConsumer(
    ISender mediator,
    IPublishEndpoint publishEndpoint,
    ILogger<RedeemSalePromotionRequestedConsumer> logger)
    : IntegrationEventConsumerBase<RedeemSalePromotionRequestedV1, RedeemSalePromotionRequestedConsumer>(logger)
{
    protected override async Task HandleAsync(RedeemSalePromotionRequestedV1 @event, CancellationToken cancellationToken)
    {
        var command = new RedeemPromotionCommand(
            CouponCode: @event.CouponCode,
            CustomerId: Guid.Parse(@event.UserId),
            OrderId: @event.OrderId,
            Subtotal: @event.Subtotal,
            Items: @event.Items.Select(i => new RedeemOrderItem(i.ProductId, i.VariantId, i.UnitPrice, i.Quantity)).ToList()
        );

        var result = await mediator.Send(command, cancellationToken);

        if (result.IsSuccess)
        {
            var itemLookup = @event.Items.ToDictionary(i => i.VariantId);

            await publishEndpoint.Publish(new PromotionRedeemedV1
            {
                OrderId = @event.OrderId,
                CorrelationId = @event.OrderId.ToString("D"),
                CausationId = @event.EventId.ToString(),
                OrderLevelDiscount = result.Value.OrderLevelDiscount,
                ItemDiscounts = result.Value.ItemDiscounts
                    .Select(d => new PromotionItemDiscount(
                        itemLookup.TryGetValue(d.VariantId, out var item) ? item.ProductId : Guid.Empty,
                        d.VariantId,
                        d.DiscountPerUnit))
                    .ToList(),
                AppliedPromotionIds = result.Value.AppliedPromotionIds.ToList(),
                ClaimedFlashSaleSlots = result.Value.ClaimedFlashSaleSlots
                    .Select(s => new ClaimedFlashSaleSlot(s.PromotionId, s.SlotKey, s.Quantity))
                    .ToList()
            }, cancellationToken);
        }
        else
        {
            await publishEndpoint.Publish(new PromotionRedeemFailedV1
            {
                OrderId = @event.OrderId,
                CorrelationId = @event.OrderId.ToString("D"),
                CausationId = @event.EventId.ToString(),
                ErrorCode = result.Error!.Code,
                ErrorMessage = result.Error!.Message
            }, cancellationToken);
        }
    }
}
