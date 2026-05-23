using Shared.Kernel.Primitives;

namespace UrbanX.Payment.Application.Abstractions;

public interface IAutoRefundService
{
    /// <summary>
    /// Creates a Refund row for the given payment, then attempts to settle it through the matching
    /// <see cref="IPaymentRefundProvider"/>. SEPay refunds stay <c>Pending</c> for manual processing;
    /// MoMo refunds are completed inline when the gateway accepts them.
    /// </summary>
    /// <returns>Refund id, or <c>null</c> when no refund was issued (e.g. amount below threshold).</returns>
    Task<Result<Guid?>> CreateAndAttemptAsync(
        Guid paymentId,
        decimal amount,
        string reason,
        bool enforceThreshold,
        CancellationToken cancellationToken);
}
