using Microsoft.AspNetCore.Http;
using Shared.Application.Authorization;
using Shared.Kernel.Constants;

namespace Shared.Messaging.Authorization;

internal sealed class UserHttpContext : IUserContext
{
    private readonly IHttpContextAccessor _accessor;

    public UserHttpContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    private HttpContext? Ctx => _accessor.HttpContext;

    public bool IsAuthenticated => UserId.HasValue;

    public Guid? UserId =>
        Guid.TryParse(Ctx?.Request.Headers[GatewayHeaderNames.XUserId], out var id)
            ? id
            : null;

    public Guid? MerchantId =>
        Guid.TryParse(Ctx?.Request.Headers[GatewayHeaderNames.XMerchantId], out var id)
            ? id
            : null;

    public IReadOnlyCollection<string> Roles
    {
        get
        {
            var raw = Ctx?.Request.Headers[GatewayHeaderNames.XUserRoles].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(raw))
                return Array.Empty<string>();
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    public PermissionScope Scope =>
        Ctx?.Request.Headers[GatewayHeaderNames.XPermissionScope].FirstOrDefault() switch
        {
            "all" => PermissionScope.All,
            "own" => PermissionScope.Own,
            _ => PermissionScope.None
        };

    public string? RequestId => Ctx?.Request.Headers[GatewayHeaderNames.XRequestId].FirstOrDefault();

    public bool HasRole(string role) =>
        Roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
}
