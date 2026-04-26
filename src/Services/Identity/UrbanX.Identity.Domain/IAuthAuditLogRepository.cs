using Shared.Kernel.Primitives;
using UrbanX.Identity.Domain.Models;

namespace UrbanX.Identity.Domain
{
    public interface IAuthAuditLogRepository
    {
        Task AddAsync(AuthAuditLog log, CancellationToken cancellationToken);
        Task<PageResult<AuthAuditLog>> ListAsync(
            Guid? userId,
            string? eventType,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken);
    }
}
