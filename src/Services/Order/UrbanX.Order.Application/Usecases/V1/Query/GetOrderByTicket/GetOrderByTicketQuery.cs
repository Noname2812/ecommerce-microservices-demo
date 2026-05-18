using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Order.Application.Usecases.V1.Query.GetOrderByTicket;

[RequirePermission(Permissions.Orders.Read, MinScope = PermissionScope.Own)]
public record GetOrderByTicketQuery(Guid TicketId) : IQuery<OrderTicketStatusDto>;

public sealed class GetOrderByTicketQueryValidator : AbstractValidator<GetOrderByTicketQuery>
{
    public GetOrderByTicketQueryValidator()
    {
        RuleFor(x => x.TicketId).NotEmpty();
    }
}

public record OrderTicketStatusDto(
    Guid TicketId,
    string Status,
    Guid? OrderId,
    string? PaymentUrl,
    string? QrCodeUrl,
    string? PaymentStatus,
    string? CancelledReason,
    DateTimeOffset? PaymentExpiresAt);
