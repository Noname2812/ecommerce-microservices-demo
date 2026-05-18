using System.Text.Json;
using Shared.Contract.Dtos.Order;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Contract.Messaging.PlaceOrderSaga;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;

namespace UrbanX.Order.Application.Usecases.V1.Command.Common;

internal static class PlaceOrderEventMappings
{
    public static OrderDtos.ShippingAddressSnapshot MapShipping(PlaceOrderShippingAddressDto address) =>
        new(
            FullName: address.FullName,
            Phone: address.Phone,
            Address: address.Address,
            Ward: address.Ward ?? string.Empty,
            District: address.District,
            City: address.City,
            Province: address.Province ?? string.Empty,
            Country: address.Country,
            ZipCode: address.ZipCode);

    public static string SerializePricingSnapshot(PlaceOrderPricingSnapshotDto snapshot) =>
        JsonSerializer.Serialize(snapshot);

    public static IReadOnlyList<NormalOrderItemSnapshot> MapNormalItems(IReadOnlyList<PlaceOrderLineDto> items) =>
        items.Select(i => new NormalOrderItemSnapshot(i.ProductId, i.VariantId, i.Quantity, i.UnitPrice)).ToList();

    public static IReadOnlyList<OrderItemSnapshot> MapSalesItems(IReadOnlyList<PlaceOrderLineDto> items) =>
        items.Select(i => new OrderItemSnapshot(i.ProductId, i.VariantId, i.Quantity, i.UnitPrice)).ToList();

    public static decimal SumLineTotal(IReadOnlyList<PlaceOrderLineDto> items) =>
        items.Sum(i => i.UnitPrice * i.Quantity);
}
