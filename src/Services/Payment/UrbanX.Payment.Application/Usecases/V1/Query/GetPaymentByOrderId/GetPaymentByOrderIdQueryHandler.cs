using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Domain.Errors;
using UrbanX.Payment.Application.Usecases.V1.Query.GetPaymentById;
using UrbanX.Payment.Domain;

namespace UrbanX.Payment.Application.Usecases.V1.Query.GetPaymentByOrderId;

public sealed class GetPaymentByOrderIdQueryHandler(IPaymentRepository paymentRepository)
    : IQueryHandler<GetPaymentByOrderIdQuery, PaymentDetailDto>
{
    public async Task<Result<PaymentDetailDto>> Handle(GetPaymentByOrderIdQuery request, CancellationToken cancellationToken)
    {
        var payment = await paymentRepository.GetByOrderIdAsync(request.OrderId, cancellationToken);
        if (payment is null)
            return Result.Failure<PaymentDetailDto>(PaymentErrors.PaymentNotFound);

        return Result.Success(new PaymentDetailDto(
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
            payment.UpdatedAt));
    }
}
