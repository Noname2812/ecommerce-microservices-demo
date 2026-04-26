using Microsoft.EntityFrameworkCore;
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Identity.Persistence;

namespace UrbanX.Identity.Application.Usecases.V1.Query;

public sealed class ListAllRolesQueryHandler : IQueryHandler<ListAllRolesQuery, IReadOnlyList<RoleDto>>
{
    private readonly IdentityDbContext _db;

    public ListAllRolesQueryHandler(IdentityDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<RoleDto>>> Handle(ListAllRolesQuery request, CancellationToken cancellationToken)
    {
        var roles = await _db.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Name!, r.Description))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<RoleDto>>(roles);
    }
}
