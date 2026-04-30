using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Order.Application.Usecases.V1.Query;

[RequirePermission(Permissions.Orders.Read, MinScope = PermissionScope.Own)]
public record GetOrderByIdQuery(Guid OrderId) : IQuery<OrderDetailDto>;

public sealed class GetOrderByIdQueryValidator : AbstractValidator<GetOrderByIdQuery>
{
    public GetOrderByIdQueryValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
    }
}
