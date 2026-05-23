using System.ComponentModel.DataAnnotations;

namespace UrbanX.Payment.Application.Configuration;

public sealed class PaymentBusinessOptions
{
    public const string SectionName = "Payment:Business";

    /// <summary>
    /// Overpayment delta (PaidAmount - Amount) above which the system creates an automatic refund.
    /// Smaller surpluses are treated as a "tip" and logged only — refund processing fees would exceed the amount.
    /// </summary>
    [Range(0, 100_000_000)]
    public decimal OverpaymentRefundThresholdVnd { get; init; } = 10_000m;
}
