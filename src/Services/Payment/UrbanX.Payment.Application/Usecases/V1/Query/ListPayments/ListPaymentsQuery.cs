using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Application.Usecases.V1.Query.GetPaymentById;

namespace UrbanX.Payment.Application.Usecases.V1.Query.ListPayments;

[RequirePermission(Permissions.Payment.Read)]
public record ListPaymentsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Status = null,
    Guid? CustomerId = null,
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null
) : IQuery<PageResult<PaymentDetailDto>>;

public sealed class ListPaymentsQueryValidator : AbstractValidator<ListPaymentsQuery>
{
    public ListPaymentsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
