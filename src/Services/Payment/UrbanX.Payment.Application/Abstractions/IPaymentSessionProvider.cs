using Shared.Kernel.Primitives;

namespace UrbanX.Payment.Application.Abstractions;

public interface IPaymentSessionProvider
{
    string Method { get; }

    Task<Result<PaymentSessionArtifact>> CreateSessionAsync(
        PaymentSessionContext context,
        CancellationToken cancellationToken);
}

public sealed record PaymentSessionContext(
    Guid PaymentId,
    Guid OrderId,
    string OrderNumber,
    decimal Amount,
    string Currency,
    Guid? CustomerId,
    string? CustomerEmail);

public sealed record PaymentSessionArtifact(
    string ProviderName,
    Guid ProviderId,
    string? QrCodeUrl,
    string? BankAccount,
    string? BankCode,
    string? TransferReference,
    string? PayUrl,
    string? Deeplink,
    string? ProviderRequestId,
    DateTimeOffset ExpiresAt);
