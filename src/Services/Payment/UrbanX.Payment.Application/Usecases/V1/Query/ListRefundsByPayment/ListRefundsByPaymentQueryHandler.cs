using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Application.Usecases.V1.Query.GetRefundById;
using UrbanX.Payment.Domain;

namespace UrbanX.Payment.Application.Usecases.V1.Query.ListRefundsByPayment;

public sealed class ListRefundsByPaymentQueryHandler(IRefundRepository refundRepository)
    : IQueryHandler<ListRefundsByPaymentQuery, IReadOnlyList<RefundDto>>
{
    public async Task<Result<IReadOnlyList<RefundDto>>> Handle(ListRefundsByPaymentQuery request, CancellationToken cancellationToken)
    {
        var refunds = await refundRepository.ListByPaymentIdAsync(request.PaymentId, cancellationToken);

        var dtos = refunds.Select(r => new RefundDto(
            r.Id, r.PaymentId, r.OrderId,
            r.Amount, r.Reason, r.ProviderRefundId,
            r.Status, r.ProcessedAt, r.CreatedAt))
            .ToList();

        return Result.Success<IReadOnlyList<RefundDto>>(dtos);
    }
}
