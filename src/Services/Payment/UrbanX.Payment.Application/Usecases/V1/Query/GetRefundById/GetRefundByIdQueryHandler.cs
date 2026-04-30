using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Application.Usecases.V1.Errors;
using UrbanX.Payment.Domain;

namespace UrbanX.Payment.Application.Usecases.V1.Query.GetRefundById;

public sealed class GetRefundByIdQueryHandler(IRefundRepository refundRepository)
    : IQueryHandler<GetRefundByIdQuery, RefundDto>
{
    public async Task<Result<RefundDto>> Handle(GetRefundByIdQuery request, CancellationToken cancellationToken)
    {
        var refund = await refundRepository.GetByIdAsync(request.RefundId, cancellationToken);
        if (refund is null)
            return Result.Failure<RefundDto>(PaymentErrors.RefundNotFound);

        return Result.Success(new RefundDto(
            refund.Id, refund.PaymentId, refund.OrderId,
            refund.Amount, refund.Reason, refund.ProviderRefundId,
            refund.Status, refund.ProcessedAt, refund.CreatedAt));
    }
}
