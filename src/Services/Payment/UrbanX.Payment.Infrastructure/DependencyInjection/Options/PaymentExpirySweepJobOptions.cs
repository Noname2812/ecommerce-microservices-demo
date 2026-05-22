using System.ComponentModel.DataAnnotations;

namespace UrbanX.Payment.Infrastructure.DependencyInjection.Options;

public sealed class PaymentExpirySweepJobOptions
{
    public const string SectionName = "Payment:Jobs:PaymentExpirySweep";

    [Required]
    public string CronExpression { get; set; } = "*/1 * * * *";
}
