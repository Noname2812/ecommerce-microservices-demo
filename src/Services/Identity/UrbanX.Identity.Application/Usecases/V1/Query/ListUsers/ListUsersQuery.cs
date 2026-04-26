using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;

namespace UrbanX.Identity.Application.Usecases.V1.Query;

[RequirePermission(Permissions.Users.Read, MinScope = PermissionScope.All)]
public record ListUsersQuery(
    int PageIndex = 1,
    int PageSize = 20,
    string? SearchTerm = null,
    string? Role = null,
    bool? IsActive = null
) : IQuery<PageResult<UserSummaryDto>>;

public sealed class ListUsersQueryValidator : AbstractValidator<ListUsersQuery>
{
    public ListUsersQueryValidator()
    {
        RuleFor(x => x.PageIndex).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PageResult<object>.UpperPageSize);
        RuleFor(x => x.SearchTerm).MaximumLength(200).When(x => x.SearchTerm is not null);
        RuleFor(x => x.Role).MaximumLength(50).When(x => x.Role is not null);
    }
}
