namespace Shared.Contract.Dtos.Order;

public static class OrderDtos
{
    public record ShippingAddressSnapshot(
        string FullName,
        string Phone,
        string Address,
        string Ward,
        string District,
        string City,
        string Province,
        string Country,
        string? ZipCode);
}
