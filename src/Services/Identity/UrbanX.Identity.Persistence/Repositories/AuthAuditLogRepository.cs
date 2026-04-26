using Microsoft.EntityFrameworkCore;
using Shared.Kernel.Primitives;
using UrbanX.Identity.Domain;
using UrbanX.Identity.Domain.Models;

namespace UrbanX.Identity.Persistence.Repositories;

public sealed class AuthAuditLogRepository : IAuthAuditLogRepository
{
    private readonly IdentityDbContext _db;

    public AuthAuditLogRepository(IdentityDbContext db) => _db = db;

    public async Task AddAsync(AuthAuditLog log, CancellationToken cancellationToken)
    {
        await _db.AuthAuditLogs.AddAsync(log, cancellationToken);
    }

    public async Task<PageResult<AuthAuditLog>> ListAsync(
        Guid? userId,
        string? eventType,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = _db.AuthAuditLogs.AsNoTracking();

        if (userId.HasValue)
            query = query.Where(x => x.UserId == userId);
        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(x => x.EventType == eventType);
        if (from.HasValue)
            query = query.Where(x => x.OccurredAt >= from);
        if (to.HasValue)
            query = query.Where(x => x.OccurredAt <= to);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.OccurredAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return PageResult<AuthAuditLog>.Create(items, pageIndex, pageSize, total);
    }
}
