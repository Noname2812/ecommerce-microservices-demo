using Shared.Kernel.Primitives;

namespace UrbanX.Payment.Application.Abstractions;

public interface IPaymentRefundProvider
{
    string Method { get; }

    /// <summary>
    /// Issues a refund against the upstream provider.
    /// <paramref name="refundId"/> MUST be stable across retries so the provider can dedup;
    /// implementations should derive the wire-level orderId/requestId from it.
    /// </summary>
    Task<Result<string>> RefundAsync(
        Guid refundId,
        Guid paymentId,
        string providerTransactionId,
        decimal amount,
        string reason,
        CancellationToken cancellationToken);
}
