using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.ValueObjects;
using OrderEntity = UrbanX.Order.Domain.Models.Order;

namespace UrbanX.Order.Application.Usecases.V1.Command.Common;

internal static class OrderFactory
{
    public static OrderEntity Build(
        IPlaceOrderRequest request,
        Guid userId,
        string orderNumber,
        string orderType = OrderType.Normal,
        Guid? campaignId = null,
        bool useItemDiscount = true)
    {
        var address = ShippingAddress.Create(
            request.ShippingAddress.Address,
            request.ShippingAddress.Ward,
            request.ShippingAddress.District,
            request.ShippingAddress.City,
            request.ShippingAddress.Province,
            request.ShippingAddress.Country,
            request.ShippingAddress.ZipCode,
            request.ShippingAddress.FullName,
            request.ShippingAddress.Phone);

        var specs = request.Items.Select(i => new NewOrderItemSpec(
            i.ProductId, i.ProductName, i.ProductSlug,
            i.VariantId, i.VariantSku, i.VariantName,
            i.SellerId, i.SellerName,
            i.UnitPrice, i.Quantity,
            useItemDiscount ? i.DiscountAmount : 0m,
            i.ImageUrl)).ToList();

        return OrderEntity.Create(
            orderNumber,
            userId,
            customerEmail: request.CustomerEmail?.Trim() ?? string.Empty,
            customerName: request.ShippingAddress.FullName,
            customerPhone: request.ShippingAddress.Phone,
            address,
            request.ShippingFee,
            request.CouponCode,
            couponDiscount: 0m,
            request.CustomerNote,
            request.IdempotencyKey,
            specs,
            orderType: orderType,
            campaignId: campaignId);
    }
}
