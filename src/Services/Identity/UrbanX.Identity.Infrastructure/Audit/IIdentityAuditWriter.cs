namespace UrbanX.Identity.Infrastructure.Audit
{
    public interface IIdentityAuditWriter
    {
        Task WriteAsync(
            Guid? userId,
            string? email,
            string eventType,
            object? metadata = null,
            CancellationToken cancellationToken = default);
    }
}
