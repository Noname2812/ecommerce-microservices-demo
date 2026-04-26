using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;

namespace UrbanX.Identity.Application.Usecases.V1.Query;

[RequirePermission(Permissions.Users.Read, MinScope = PermissionScope.All)]
public record ListAuditLogsQuery(
    Guid? UserId,
    string? EventType,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int PageIndex = 1,
    int PageSize = 50
) : IQuery<PageResult<AuthAuditLogDto>>;

public sealed class ListAuditLogsQueryValidator : AbstractValidator<ListAuditLogsQuery>
{
    public ListAuditLogsQueryValidator()
    {
        RuleFor(x => x.PageIndex).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PageResult<object>.UpperPageSize);
        RuleFor(x => x).Must(q => !q.From.HasValue || !q.To.HasValue || q.From <= q.To)
            .WithMessage("From must be before To");
    }
}
