using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Application.Usecases.V1.Query.GetPaymentById;
using UrbanX.Payment.Domain;

namespace UrbanX.Payment.Application.Usecases.V1.Query.ListPayments;

public sealed class ListPaymentsQueryHandler(IPaymentRepository paymentRepository)
    : IQueryHandler<ListPaymentsQuery, PageResult<PaymentDetailDto>>
{
    public async Task<Result<PageResult<PaymentDetailDto>>> Handle(ListPaymentsQuery request, CancellationToken cancellationToken)
    {
        var page = await paymentRepository.ListAsync(
            request.Page,
            request.PageSize,
            request.Status,
            request.CustomerId,
            request.FromDate,
            request.ToDate,
            cancellationToken);

        var dtos = page.Items.Select(p => new PaymentDetailDto(
            p.Id, p.OrderId, p.OrderNumber, p.CustomerId, p.CustomerEmail,
            p.ProviderName, p.Amount, p.Currency, p.ProviderTransactionId,
            p.Status, p.FailureReason, p.PaidAt, p.CreatedAt, p.UpdatedAt))
            .ToList();

        return Result.Success(PageResult<PaymentDetailDto>.Create(dtos, page.PageIndex, page.PageSize, page.TotalCount));
    }
}
