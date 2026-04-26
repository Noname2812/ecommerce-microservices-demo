using System.Text.Json;
using Microsoft.AspNetCore.Http;
using UrbanX.Identity.Domain;
using UrbanX.Identity.Domain.Models;

namespace UrbanX.Identity.Infrastructure.Audit
{
    public sealed class IdentityAuditWriter : IIdentityAuditWriter
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly IAuthAuditLogRepository _repository;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public IdentityAuditWriter(IAuthAuditLogRepository repository, IHttpContextAccessor httpContextAccessor)
        {
            _repository = repository;
            _httpContextAccessor = httpContextAccessor;
        }

        public Task WriteAsync(
            Guid? userId,
            string? email,
            string eventType,
            object? metadata = null,
            CancellationToken cancellationToken = default)
        {
            var ctx = _httpContextAccessor.HttpContext;
            var ip = ctx?.Connection.RemoteIpAddress?.ToString();
            var ua = ctx?.Request.Headers.UserAgent.ToString();

            var log = new AuthAuditLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Email = email,
                EventType = eventType,
                IpAddress = string.IsNullOrEmpty(ip) ? null : ip,
                UserAgent = string.IsNullOrEmpty(ua) ? null : ua,
                MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata, SerializerOptions),
                OccurredAt = DateTimeOffset.UtcNow
            };

            return _repository.AddAsync(log, cancellationToken);
        }
    }
}
