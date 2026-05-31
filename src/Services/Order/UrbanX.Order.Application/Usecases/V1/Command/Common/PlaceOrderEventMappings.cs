using System.Text.Json;
using Shared.Contract.Dtos.Order;
using Shared.Contract.Messaging.PlaceOrder;
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
        items.Select(i => new NormalOrderItemSnapshot(i.ProductId, i.VariantId, i.Quantity, i.UnitPrice, i.Version)).ToList();

    public static IReadOnlyList<SalesOrderItemSnapshot> MapSalesItems(IReadOnlyList<PlaceOrderLineDto> items) =>
        items.Select(i => new SalesOrderItemSnapshot(i.ProductId, i.VariantId, i.Quantity, i.UnitPrice, i.Version)).ToList();

    public static decimal SumLineTotal(IReadOnlyList<PlaceOrderLineDto> items) =>
        items.Sum(i => i.UnitPrice * i.Quantity);
}
