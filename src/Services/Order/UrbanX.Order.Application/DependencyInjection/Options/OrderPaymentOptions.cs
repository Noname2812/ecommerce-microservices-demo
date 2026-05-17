namespace UrbanX.Order.Application.DependencyInjection.Options;

public sealed class OrderPaymentOptions
{
    public const string SectionName = "Order:Payment";

    public int NormalOrderExpiryMinutes { get; init; } = 30;
    public int SalesOrderExpiryMinutes { get; init; } = 15;
}
