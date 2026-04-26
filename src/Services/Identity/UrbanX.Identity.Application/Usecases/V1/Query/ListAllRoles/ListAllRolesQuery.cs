using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Identity.Application.Usecases.V1.Query;

[RequirePermission(Permissions.Roles.Read, MinScope = PermissionScope.All)]
public record ListAllRolesQuery() : IQuery<IReadOnlyList<RoleDto>>;
