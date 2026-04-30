using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using UrbanX.Payment.Application.Usecases.V1.Query.GetRefundById;

namespace UrbanX.Payment.Application.Usecases.V1.Query.ListRefundsByPayment;

[RequirePermission(Permissions.Payment.Read)]
public record ListRefundsByPaymentQuery(Guid PaymentId) : IQuery<IReadOnlyList<RefundDto>>;

public sealed class ListRefundsByPaymentQueryValidator : AbstractValidator<ListRefundsByPaymentQuery>
{
    public ListRefundsByPaymentQueryValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
    }
}
