using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;
using UrbanX.Identity.Application.Usecases.V1.Errors;
using UrbanX.Identity.Domain.Models;

namespace UrbanX.Identity.Application.Usecases.V1.Query;

public sealed class GetUserByIdQueryHandler : IQueryHandler<GetUserByIdQuery, UserProfileDto>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IUserContext _userContext;

    public GetUserByIdQueryHandler(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IUserContext userContext)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _userContext = userContext;
    }

    public async Task<Result<UserProfileDto>> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        if (_userContext.Scope == PermissionScope.Own && _userContext.UserId != request.UserId)
            return Result.Failure<UserProfileDto>(AuthorizationErrors.MissingPermission(Permissions.Users.Read));

        var user = await _userManager.Users
            .AsNoTracking()
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

        if (user is null)
            return Result.Failure<UserProfileDto>(AuthErrors.UserNotFound(request.UserId));

        var roles = await _userManager.GetRolesAsync(user);
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var roleName in roles)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role is null) continue;
            foreach (var c in (await _roleManager.GetClaimsAsync(role)).Where(c => c.Type == "permission"))
                permissions.Add(c.Value);
        }

        return Result.Success(new UserProfileDto(
            user.Id,
            user.Email!,
            user.DisplayName,
            user.PhoneNumber,
            user.AvatarUrl,
            user.MerchantId,
            user.IsActive,
            user.EmailConfirmed,
            user.CreatedAt,
            roles.ToList(),
            permissions.ToList(),
            user.Profile?.Bio,
            user.Profile?.DateOfBirth,
            user.Profile?.Gender,
            user.Profile?.AddressLine,
            user.Profile?.City,
            user.Profile?.Country,
            user.Profile?.PostalCode
        ));
    }
}
