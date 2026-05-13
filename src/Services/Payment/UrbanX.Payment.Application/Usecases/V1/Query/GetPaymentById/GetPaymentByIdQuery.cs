using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Payment.Application.Usecases.V1.Query.GetPaymentById;

[RequirePermission(Permissions.Payment.Read)]
public record GetPaymentByIdQuery(Guid PaymentId) : IQuery<PaymentDetailDto>;

public sealed class GetPaymentByIdQueryValidator : AbstractValidator<GetPaymentByIdQuery>
{
    public GetPaymentByIdQueryValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
    }
}

public record PaymentDetailDto(
    Guid Id,
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    string CustomerEmail,
    string ProviderName,
    decimal Amount,
    decimal PaidAmount,
    decimal RemainingAmount,
    string Currency,
    string? ProviderTransactionId,
    string Status,
    string? FailureReason,
    DateTimeOffset? PaidAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
