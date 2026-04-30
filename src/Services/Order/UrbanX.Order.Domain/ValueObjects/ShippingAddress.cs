namespace UrbanX.Order.Domain.ValueObjects;

public sealed class ShippingAddress
{
    public string Street { get; private set; } = null!;
    public string? Ward { get; private set; }
    public string District { get; private set; } = null!;
    public string City { get; private set; } = null!;
    public string? Province { get; private set; }
    public string Country { get; private set; } = null!;
    public string? ZipCode { get; private set; }
    public string RecipientName { get; private set; } = null!;
    public string RecipientPhone { get; private set; } = null!;

    private ShippingAddress() { }

    public static ShippingAddress Create(
        string street,
        string? ward,
        string district,
        string city,
        string? province,
        string country,
        string? zipCode,
        string recipientName,
        string recipientPhone) => new()
    {
        Street = street,
        Ward = ward,
        District = district,
        City = city,
        Province = province,
        Country = country,
        ZipCode = zipCode,
        RecipientName = recipientName,
        RecipientPhone = recipientPhone
    };
}
