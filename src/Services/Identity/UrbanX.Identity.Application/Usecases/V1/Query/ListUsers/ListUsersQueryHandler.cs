using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Identity.Domain.Models;
using UrbanX.Identity.Persistence;

namespace UrbanX.Identity.Application.Usecases.V1.Query;

public sealed class ListUsersQueryHandler : IQueryHandler<ListUsersQuery, PageResult<UserSummaryDto>>
{
    private readonly IdentityDbContext _db;

    public ListUsersQueryHandler(IdentityDbContext db) => _db = db;

    public async Task<Result<PageResult<UserSummaryDto>>> Handle(ListUsersQuery request, CancellationToken cancellationToken)
    {
        var baseQuery = _db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLowerInvariant();
            baseQuery = baseQuery.Where(u =>
                u.NormalizedEmail!.Contains(term.ToUpper()) ||
                u.DisplayName.ToLower().Contains(term));
        }

        if (request.IsActive.HasValue)
            baseQuery = baseQuery.Where(u => u.IsActive == request.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            var roleName = request.Role.ToUpperInvariant();
            var roleId = await _db.Roles
                .Where(r => r.NormalizedName == roleName)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (roleId is null)
                return Result.Success(PageResult<UserSummaryDto>.Create([], request.PageIndex, request.PageSize, 0));

            var userIds = _db.UserRoles.Where(ur => ur.RoleId == roleId.Value).Select(ur => ur.UserId);
            baseQuery = baseQuery.Where(u => userIds.Contains(u.Id));
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var users = await baseQuery
            .OrderByDescending(u => u.CreatedAt)
            .Skip((request.PageIndex - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.DisplayName,
                u.PhoneNumber,
                u.AvatarUrl,
                u.MerchantId,
                u.IsActive,
                u.EmailConfirmed,
                u.CreatedAt,
                Roles = (from ur in _db.UserRoles
                         join r in _db.Roles on ur.RoleId equals r.Id
                         where ur.UserId == u.Id
                         select r.Name!).ToList()
            })
            .ToListAsync(cancellationToken);

        var items = users.Select(u => new UserSummaryDto(
            u.Id, u.Email!, u.DisplayName, u.PhoneNumber, u.AvatarUrl,
            u.MerchantId, u.IsActive, u.EmailConfirmed, u.CreatedAt, u.Roles
        )).ToList();

        return Result.Success(PageResult<UserSummaryDto>.Create(items, request.PageIndex, request.PageSize, totalCount));
    }
}
