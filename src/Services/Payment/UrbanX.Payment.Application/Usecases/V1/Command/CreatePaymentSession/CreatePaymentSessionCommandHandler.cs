using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Application.Configuration;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.Errors;
using UrbanX.Payment.Domain.ValueObjects;
using PaymentEntity = UrbanX.Payment.Domain.Models.Payment;

namespace UrbanX.Payment.Application.Usecases.V1.Command.CreatePaymentSession;

internal sealed class CreatePaymentSessionCommandHandler(
    IPaymentRepository paymentRepository,
    IPaymentProviderRepository paymentProviderRepository,
    IOptionsSnapshot<SePayOptions> sePayOptions,
    ILogger<CreatePaymentSessionCommandHandler> logger)
    : ICommandHandler<CreatePaymentSessionCommand, CreatePaymentSessionResult>
{
    private const string QrEndpoint = "https://qr.sepay.vn/img";
    private const string SePayProviderName = "SePay";

    public async Task<Result<CreatePaymentSessionResult>> Handle(
        CreatePaymentSessionCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await paymentRepository.GetByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation(
                "SePay payment session idempotent hit. PaymentId={PaymentId} IdempotencyKey={IdempotencyKey}",
                existing.Id, request.IdempotencyKey);

            return Result.Success(new CreatePaymentSessionResult(
                existing.Id,
                existing.QrCodeUrl ?? string.Empty,
                existing.BankAccount ?? string.Empty,
                existing.BankCode ?? string.Empty,
                existing.TransferReference ?? existing.OrderNumber,
                existing.ExpiresAt ?? DateTimeOffset.UtcNow));
        }

        var provider = await paymentProviderRepository.GetActiveByTypeAsync(ProviderType.Sepay, cancellationToken);
        if (provider is null)
        {
            logger.LogWarning("Active SePay PaymentProvider not found (type={ProviderType}).", ProviderType.Sepay);
            return Result.Failure<CreatePaymentSessionResult>(PaymentErrors.ProviderNotFound);
        }

        var opts = sePayOptions.Value;
        var transferReference = request.OrderNumber;
        var qrUrl = BuildVietQrUrl(opts, request.Amount, transferReference);
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(opts.PaymentSessionExpiresAfterMinutes);

        var payment = new PaymentEntity
        {
            Id = Guid.NewGuid(),
            OrderId = request.OrderId,
            OrderNumber = request.OrderNumber,
            CustomerId = request.CustomerId ?? Guid.Empty,
            CustomerEmail = request.CustomerEmail ?? string.Empty,
            ProviderId = provider.Id,
            ProviderName = SePayProviderName,
            Amount = request.Amount,
            PaidAmount = 0m,
            RemainingAmount = request.Amount,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? PaymentCurrency.Vnd : request.Currency,
            Status = PaymentStatus.Pending,
            IdempotencyKey = request.IdempotencyKey,
            BankAccount = opts.BankAccount,
            BankCode = opts.BankCode,
            TransferReference = transferReference,
            QrCodeUrl = qrUrl,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            UpdatedAt = now
        };

        await paymentRepository.AddAsync(payment, cancellationToken);

        logger.LogInformation(
            "Created SePay payment session. PaymentId={PaymentId} OrderId={OrderId} Amount={Amount} Reference={Reference}",
            payment.Id, payment.OrderId, payment.Amount, transferReference);

        return Result.Success(new CreatePaymentSessionResult(
            payment.Id, qrUrl, opts.BankAccount, opts.BankCode, transferReference, expiresAt));
    }

    private static string BuildVietQrUrl(SePayOptions opts, decimal amount, string transferReference)
    {
        var amountStr = amount.ToString("0.##", CultureInfo.InvariantCulture);
        var qs =
            $"acc={Uri.EscapeDataString(opts.BankAccount)}" +
            $"&bank={Uri.EscapeDataString(opts.BankCode)}" +
            $"&amount={Uri.EscapeDataString(amountStr)}" +
            $"&des={Uri.EscapeDataString(transferReference)}" +
            $"&template={Uri.EscapeDataString(opts.QrTemplate)}";
        return $"{QrEndpoint}?{qs}";
    }
}
