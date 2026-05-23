using System.ComponentModel.DataAnnotations;

namespace UrbanX.Order.Infrastructure.DependencyInjection.Options;

public sealed class OrderPaymentOptions
{
    public const string SectionName = "Order:Payment";

    [Range(1, 1440)]
    public int NormalOrderExpiryMinutes { get; init; } = 30;

    [Range(1, 1440)]
    public int SalesOrderExpiryMinutes { get; init; } = 15;

    /// <summary>
    /// Per-step timeout (catalog response, inventory ack, payment-session creation, etc.) for sagas
    /// that schedule <c>StepTimeout</c>. Anything longer than this is considered a step failure.
    /// </summary>
    [Range(1, 600)]
    public int StepTimeoutSeconds { get; init; } = 30;
}
