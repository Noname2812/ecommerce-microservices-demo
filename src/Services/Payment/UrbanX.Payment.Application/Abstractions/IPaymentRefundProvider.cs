using Shared.Kernel.Primitives;

namespace UrbanX.Payment.Application.Abstractions;

public interface IPaymentRefundProvider
{
    string Method { get; }

    Task<Result<string>> RefundAsync(
        Guid paymentId,
        string providerTransactionId,
        decimal amount,
        string reason,
        CancellationToken cancellationToken);
}
