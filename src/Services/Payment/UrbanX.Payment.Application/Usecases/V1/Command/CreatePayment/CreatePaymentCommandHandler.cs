using Microsoft.Extensions.Options;
using UrbanX.Payment.Application.Configuration;
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Domain.Errors;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.ValueObjects;
using PaymentEntity = UrbanX.Payment.Domain.Models.Payment;

namespace UrbanX.Payment.Application.Usecases.V1.Command.CreatePayment;

public sealed class CreatePaymentCommandHandler(
    IPaymentRepository paymentRepository,
    IPaymentProviderRepository providerRepository,
    IOptionsSnapshot<SePayOptions> sePayOptions) : ICommandHandler<CreatePaymentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreatePaymentCommand request, CancellationToken cancellationToken)
    {
        var existing = await paymentRepository.GetByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
        if (existing is not null)
            return Result.Success(existing.Id);

        var provider = await providerRepository.GetActiveByTypeAsync(ProviderType.Sepay, cancellationToken)
            ?? await providerRepository.GetActiveByTypeAsync(ProviderType.Cod, cancellationToken);
        if (provider is null)
            return Result.Failure<Guid>(PaymentErrors.ProviderNotFound);

        var opts = sePayOptions.Value;
        var expiryHours = opts.PaymentExpiresAfterHours;
        if (expiryHours < opts.PaymentExpiresAfterHoursMinimum)
            expiryHours = opts.PaymentExpiresAfterHoursFallback;

        var payment = new PaymentEntity
        {
            Id = Guid.NewGuid(),
            OrderId = request.OrderId,
            OrderNumber = request.OrderNumber,
            CustomerId = request.CustomerId,
            CustomerEmail = request.CustomerEmail,
            ProviderId = provider.Id,
            ProviderName = provider.Name,
            Amount = request.TotalAmount,
            PaidAmount = 0,
            RemainingAmount = request.TotalAmount,
            Currency = PaymentCurrency.Vnd,
            IdempotencyKey = request.IdempotencyKey,
            IpAddress = request.IpAddress,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(expiryHours),
        };

        await paymentRepository.AddAsync(payment, cancellationToken);

        return Result.Success(payment.Id);
    }
}
