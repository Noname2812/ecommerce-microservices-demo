using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Domain.Errors;
using UrbanX.Payment.Domain;

namespace UrbanX.Payment.Application.Usecases.V1.Query.GetPaymentById;

public sealed class GetPaymentByIdQueryHandler(IPaymentRepository paymentRepository)
    : IQueryHandler<GetPaymentByIdQuery, PaymentDetailDto>
{
    public async Task<Result<PaymentDetailDto>> Handle(GetPaymentByIdQuery request, CancellationToken cancellationToken)
    {
        var payment = await paymentRepository.GetByIdAsync(request.PaymentId, cancellationToken);
        if (payment is null)
            return Result.Failure<PaymentDetailDto>(PaymentErrors.PaymentNotFound);

        var dto = new PaymentDetailDto(
            payment.Id,
            payment.OrderId,
            payment.OrderNumber,
            payment.CustomerId,
            payment.CustomerEmail,
            payment.ProviderName,
            payment.Amount,
            payment.PaidAmount,
            payment.RemainingAmount,
            payment.Currency,
            payment.ProviderTransactionId,
            payment.Status,
            payment.FailureReason,
            payment.PaidAt,
            payment.ExpiresAt,
            payment.CreatedAt,
            payment.UpdatedAt);

        return Result.Success(dto);
    }
}
