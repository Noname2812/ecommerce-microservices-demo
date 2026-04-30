using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;
using UrbanX.Identity.Application.Usecases.V1.Errors;
using UrbanX.Identity.Domain.Models;

namespace UrbanX.Identity.Application.Usecases.V1.Query;

public sealed class GetCurrentUserQueryHandler : IQueryHandler<GetCurrentUserQuery, UserProfileDto>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IUserContext _userContext;

    public GetCurrentUserQueryHandler(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IUserContext userContext)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _userContext = userContext;
    }

    public async Task<Result<UserProfileDto>> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        if (!_userContext.IsAuthenticated || _userContext.UserId is null)
            return Result.Failure<UserProfileDto>(AuthErrors.InvalidCredentials);

        var userId = _userContext.UserId.Value;
        var user = await _userManager.Users
            .AsNoTracking()
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
            return Result.Failure<UserProfileDto>(AuthErrors.UserNotFound(userId));

        var roles = await _userManager.GetRolesAsync(user);
        var permissions = await CollectPermissionsAsync(roles, cancellationToken);

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
            permissions,
            user.Profile?.Bio,
            user.Profile?.DateOfBirth,
            user.Profile?.Gender,
            user.Profile?.AddressLine,
            user.Profile?.City,
            user.Profile?.Country,
            user.Profile?.PostalCode
        ));
    }

    private async Task<List<string>> CollectPermissionsAsync(IList<string> roles, CancellationToken cancellationToken)
    {
        var perms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var roleName in roles)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role is null) continue;
            var claims = await _roleManager.GetClaimsAsync(role);
            foreach (var c in claims.Where(c => c.Type == "permission"))
                perms.Add(c.Value);
        }
        return perms.ToList();
    }
}
