using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Identity.Application.Usecases.V1.Query;

[RequirePermission(Permissions.Users.Read, MinScope = PermissionScope.Own)]
public record GetUserByIdQuery(Guid UserId) : IQuery<UserProfileDto>;

public sealed class GetUserByIdQueryValidator : AbstractValidator<GetUserByIdQuery>
{
    public GetUserByIdQueryValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}
