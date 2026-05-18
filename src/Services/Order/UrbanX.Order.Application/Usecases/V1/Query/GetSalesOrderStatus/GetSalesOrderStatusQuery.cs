using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Order.Application.Usecases.V1.Query.GetSalesOrderStatus;

[RequirePermission(Permissions.Orders.Read, MinScope = PermissionScope.Own)]
public record GetSalesOrderStatusQuery(Guid OrderId) : IQuery<SalesOrderStatusDto>;

public sealed class GetSalesOrderStatusQueryValidator : AbstractValidator<GetSalesOrderStatusQuery>
{
    public GetSalesOrderStatusQueryValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
    }
}

public record SalesOrderStatusDto(
    Guid OrderId,
    string OrderStatus,
    string SagaState,
    Guid? ReservationId,
    Guid? CouponClaimId,
    bool CouponLocked,
    string? FailureStep,
    string? FailureReason,
    DateTimeOffset UpdatedAt);
