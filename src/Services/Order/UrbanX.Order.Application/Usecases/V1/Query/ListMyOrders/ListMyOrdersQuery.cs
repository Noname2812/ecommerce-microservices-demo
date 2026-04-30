using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;

namespace UrbanX.Order.Application.Usecases.V1.Query;

[RequirePermission(Permissions.Orders.Read, MinScope = PermissionScope.Own)]
public record ListMyOrdersQuery(int Page = 1, int PageSize = 10) : IQuery<PageResult<OrderSummaryDto>>;

public sealed class ListMyOrdersQueryValidator : AbstractValidator<ListMyOrdersQuery>
{
    public ListMyOrdersQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize)
            .GreaterThanOrEqualTo(1)
            .LessThanOrEqualTo(PageResult<object>.UpperPageSize);
    }
}
