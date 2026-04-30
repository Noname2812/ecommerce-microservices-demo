using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using UrbanX.Payment.Application.Usecases.V1.Query.GetPaymentById;

namespace UrbanX.Payment.Application.Usecases.V1.Query.GetPaymentByOrderId;

[RequirePermission(Permissions.Payment.Read)]
public record GetPaymentByOrderIdQuery(Guid OrderId) : IQuery<PaymentDetailDto>;

public sealed class GetPaymentByOrderIdQueryValidator : AbstractValidator<GetPaymentByOrderIdQuery>
{
    public GetPaymentByOrderIdQueryValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
    }
}
