using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Payment.Application.Usecases.V1.Query.GetRefundById;

[RequirePermission(Permissions.Payment.Read)]
public record GetRefundByIdQuery(Guid RefundId) : IQuery<RefundDto>;

public sealed class GetRefundByIdQueryValidator : AbstractValidator<GetRefundByIdQuery>
{
    public GetRefundByIdQueryValidator()
    {
        RuleFor(x => x.RefundId).NotEmpty();
    }
}

public record RefundDto(
    Guid Id,
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string? Reason,
    string? ProviderRefundId,
    string Status,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset CreatedAt
);
