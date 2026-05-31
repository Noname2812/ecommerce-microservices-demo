using System.Text.Json;
using Shared.Contract.Dtos.Order;
using Shared.Contract.Messaging.PlaceOrder;
using UrbanX.Order.Application.Sagas.PlaceOrderNormal;
using UrbanX.Order.Application.Sagas.PlaceOrderSales;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.ValueObjects;
using OrderEntity = UrbanX.Order.Domain.Models.Order;

namespace UrbanX.Order.Application.Usecases.V1.Command.Common;

/// <summary>Pricing inputs for <see cref="OrderFactory.BuildSalesFromSaga"/> — all server-computed.</summary>
public record SalesPricingSnapshot(
    decimal OriginalPrice,
    decimal SaleDiscount,
    decimal CouponDiscount,
    decimal ShippingFee,
    decimal FinalTotal);

internal static class OrderFactory
{
    public static OrderEntity Build(
        IPlaceOrderRequest request,
        Guid userId,
        Guid orderId,
        string orderNumber,
        string orderType = OrderType.Normal,
        Guid? campaignId = null,
        bool useItemDiscount = true,
        decimal saleDiscount = 0m,
        decimal? originalPrice = null)
    {
        var address = MapShippingAddress(request.ShippingAddress);

        var specs = request.Items.Select(i => new NewOrderItemSpec(
            i.ProductId, i.ProductName, i.ProductSlug,
            i.VariantId, i.VariantSku, i.VariantName,
            i.SellerId, i.SellerName,
            i.UnitPrice, i.Quantity,
            useItemDiscount ? i.DiscountAmount : 0m,
            i.ImageUrl)).ToList();

        var preDiscountTotal = originalPrice ?? request.Items.Sum(i => i.UnitPrice * i.Quantity);

        return OrderEntity.Create(
            orderId,
            orderNumber,
            userId,
            request.CustomerEmail?.Trim() ?? string.Empty,
            request.ShippingAddress.FullName,
            request.ShippingAddress.Phone,
            address,
            request.ShippingFee,
            request.CouponCode,
            couponDiscount: 0m,
            saleDiscount,
            preDiscountTotal,
            request.CustomerNote,
            request.IdempotencyKey,
            specs,
            orderType,
            campaignId,
            paymentMethod: request.PaymentMethod.ToString());
    }

    public static OrderEntity BuildFromSaga(
        PlaceOrderNormalSagaState saga,
        IDictionary<Guid, ProductVariantReadModel> variants,
        Guid orderId)
    {
        var snapshots = JsonSerializer.Deserialize<List<NormalOrderItemSnapshot>>(saga.ItemsJson ?? "[]")
            ?? throw new InvalidOperationException("ItemsJson is null or invalid.");

        if (snapshots.Count == 0)
            throw new InvalidOperationException("ItemsJson must contain at least one item.");

        var items = snapshots
            .Select(i =>
            {
                if (!variants.TryGetValue(i.VariantId, out var variant))
                    throw new InvalidOperationException($"Read-model variant {i.VariantId} was not found.");

                return new NewOrderItemSpec(
                    variant.ProductId,
                    variant.ProductName,
                    ProductSlug: null,
                    variant.VariantId,
                    variant.Sku,
                    variant.VariantName,
                    variant.SellerId,
                    variant.SellerName,
                    i.UnitPrice,
                    i.Quantity,
                    DiscountAmount: 0m,
                    variant.ImageUrl);
            })
            .ToList();

        var shipping = DeserializeShipping(saga.ShippingAddressJson);
        var userId = Guid.Parse(saga.UserId);
        var subtotal = items.Sum(i => i.UnitPrice * i.Quantity);

        return OrderEntity.Create(
            orderId,
            OrderNumberGenerator.Generate("ORD"),
            userId,
            saga.CustomerEmail,
            saga.CustomerName,
            saga.CustomerPhone,
            shipping,
            saga.ShippingFee,
            saga.CouponCode,
            couponDiscount: saga.CouponDiscount,
            saleDiscount: 0m,
            originalPrice: subtotal,
            saga.CustomerNote,
            saga.IdempotencyKey,
            items,
            OrderType.Normal,
            paymentMethod: saga.PaymentMethod.ToString());
    }

    public static OrderEntity BuildSalesFromSaga(
        PlaceSalesOrderSagaState saga,
        IDictionary<Guid, ProductVariantReadModel> variants,
        SalesPricingSnapshot pricing,
        Guid orderId)
    {
        var snapshots = JsonSerializer.Deserialize<List<SalesOrderItemSnapshot>>(saga.ItemsJson ?? "[]")
            ?? throw new InvalidOperationException("ItemsJson is null or invalid.");

        if (snapshots.Count == 0)
            throw new InvalidOperationException("ItemsJson must contain at least one item.");

        var items = snapshots
            .Select(i =>
            {
                if (!variants.TryGetValue(i.VariantId, out var variant))
                    throw new InvalidOperationException($"Read-model variant {i.VariantId} was not found.");

                // Use read-model price as authoritative — server-side pricing.
                return new NewOrderItemSpec(
                    variant.ProductId,
                    variant.ProductName,
                    ProductSlug: null,
                    variant.VariantId,
                    variant.Sku,
                    variant.VariantName,
                    variant.SellerId,
                    variant.SellerName,
                    variant.Price,
                    i.Quantity,
                    DiscountAmount: 0m,
                    variant.ImageUrl);
            })
            .ToList();

        var shipping = DeserializeShipping(saga.ShippingAddressJson);
        var userId   = Guid.Parse(saga.UserId);

        return OrderEntity.Create(
            orderId,
            OrderNumberGenerator.Generate("SAL"),
            userId,
            saga.CustomerEmail,
            saga.CustomerName,
            saga.CustomerPhone,
            shipping,
            pricing.ShippingFee,
            saga.CouponCode,
            couponDiscount: pricing.CouponDiscount,
            saleDiscount:   pricing.SaleDiscount,
            originalPrice:  pricing.OriginalPrice,
            saga.CustomerNote,
            saga.IdempotencyKey,
            items,
            OrderType.Sales,
            campaignId: saga.CampaignId,
            paymentMethod: saga.PaymentMethod.ToString());
    }

    private static ShippingAddress MapShippingAddress(PlaceOrderShippingAddressDto dto) =>
        ShippingAddress.Create(
            dto.Address,
            dto.Ward,
            dto.District,
            dto.City,
            dto.Province,
            dto.Country,
            dto.ZipCode,
            dto.FullName,
            dto.Phone);

    private static ShippingAddress DeserializeShipping(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Shipping address is required to build an order from saga state.");

        var snapshot = JsonSerializer.Deserialize<OrderDtos.ShippingAddressSnapshot>(json)!;
        return ShippingAddress.Create(
            snapshot.Address,
            string.IsNullOrWhiteSpace(snapshot.Ward) ? null : snapshot.Ward,
            snapshot.District,
            snapshot.City,
            string.IsNullOrWhiteSpace(snapshot.Province) ? null : snapshot.Province,
            snapshot.Country,
            snapshot.ZipCode,
            snapshot.FullName,
            snapshot.Phone);
    }
}
