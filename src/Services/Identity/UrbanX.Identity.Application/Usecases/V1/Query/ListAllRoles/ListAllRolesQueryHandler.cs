using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Identity.Domain.Models;

namespace UrbanX.Identity.Application.Usecases.V1.Query;

public sealed class ListAllRolesQueryHandler : IQueryHandler<ListAllRolesQuery, IReadOnlyList<RoleDto>>
{
    private readonly RoleManager<ApplicationRole> _roleManager;

    public ListAllRolesQueryHandler(RoleManager<ApplicationRole> roleManager) => _roleManager = roleManager;

    public async Task<Result<IReadOnlyList<RoleDto>>> Handle(ListAllRolesQuery request, CancellationToken cancellationToken)
    {
        var roles = await _roleManager.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Name!, r.Description))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<RoleDto>>(roles);
    }
}
