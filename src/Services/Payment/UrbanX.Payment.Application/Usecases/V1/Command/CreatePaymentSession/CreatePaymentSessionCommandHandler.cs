using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Contract.Dtos.Payment;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Application.Abstractions;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.Errors;
using UrbanX.Payment.Domain.ValueObjects;
using PaymentEntity = UrbanX.Payment.Domain.Models.Payment;

namespace UrbanX.Payment.Application.Usecases.V1.Command.CreatePaymentSession;

internal sealed class CreatePaymentSessionCommandHandler(
    IPaymentRepository paymentRepository,
    IEnumerable<IPaymentSessionProvider> sessionProviders,
    ILogger<CreatePaymentSessionCommandHandler> logger)
    : ICommandHandler<CreatePaymentSessionCommand, CreatePaymentSessionResult>
{
    public async Task<Result<CreatePaymentSessionResult>> Handle(
        CreatePaymentSessionCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await paymentRepository.GetByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation(
                "Payment session idempotent hit. PaymentId={PaymentId} IdempotencyKey={IdempotencyKey}",
                existing.Id, request.IdempotencyKey);

            return Result.Success(new CreatePaymentSessionResult(
                existing.Id,
                existing.ProviderName,
                existing.QrCodeUrl,
                existing.BankAccount,
                existing.BankCode,
                existing.TransferReference ?? existing.OrderNumber,
                existing.PayUrl,
                Deeplink: null,
                existing.ExpiresAt ?? DateTimeOffset.UtcNow));
        }

        var providerCode = request.PaymentMethod.ToProviderTypeCode();
        var provider = sessionProviders.FirstOrDefault(p =>
            string.Equals(p.Method, providerCode, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            logger.LogWarning(
                "No payment session provider registered for method {Method}.", request.PaymentMethod);
            return Result.Failure<CreatePaymentSessionResult>(PaymentErrors.UnsupportedPaymentMethod);
        }

        var paymentId = Guid.NewGuid();
        var context = new PaymentSessionContext(
            PaymentId: paymentId,
            OrderId: request.OrderId,
            OrderNumber: request.OrderNumber,
            Amount: request.Amount,
            Currency: string.IsNullOrWhiteSpace(request.Currency) ? PaymentCurrency.Vnd : request.Currency,
            CustomerId: request.CustomerId,
            CustomerEmail: request.CustomerEmail);

        var artifactResult = await provider.CreateSessionAsync(context, cancellationToken);
        if (artifactResult.IsFailure)
            return Result.Failure<CreatePaymentSessionResult>(artifactResult.Error);

        var artifact = artifactResult.Value;
        var now = DateTimeOffset.UtcNow;

        var payment = new PaymentEntity
        {
            Id = paymentId,
            OrderId = request.OrderId,
            OrderNumber = request.OrderNumber,
            CustomerId = request.CustomerId ?? Guid.Empty,
            CustomerEmail = request.CustomerEmail ?? string.Empty,
            ProviderId = artifact.ProviderId,
            ProviderName = artifact.ProviderName,
            ProviderTransactionId = artifact.ProviderRequestId,
            Amount = request.Amount,
            PaidAmount = 0m,
            RemainingAmount = request.Amount,
            Currency = context.Currency,
            Status = PaymentStatus.Pending,
            IdempotencyKey = request.IdempotencyKey,
            BankAccount = artifact.BankAccount,
            BankCode = artifact.BankCode,
            TransferReference = artifact.TransferReference,
            QrCodeUrl = artifact.QrCodeUrl,
            PayUrl = artifact.PayUrl,
            ExpiresAt = artifact.ExpiresAt,
            CreatedAt = now,
            UpdatedAt = now
        };

        await paymentRepository.AddAsync(payment, cancellationToken);

        logger.LogInformation(
            "Created payment session via {Provider}. PaymentId={PaymentId} OrderId={OrderId} Amount={Amount}",
            artifact.ProviderName, payment.Id, payment.OrderId, payment.Amount);

        return Result.Success(new CreatePaymentSessionResult(
            payment.Id,
            artifact.ProviderName,
            artifact.QrCodeUrl,
            artifact.BankAccount,
            artifact.BankCode,
            artifact.TransferReference,
            artifact.PayUrl,
            artifact.Deeplink,
            artifact.ExpiresAt));
    }
}
