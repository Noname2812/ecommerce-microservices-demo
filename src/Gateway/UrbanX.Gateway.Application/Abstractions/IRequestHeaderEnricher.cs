using Microsoft.AspNetCore.Http;

namespace UrbanX.Gateway.Application.Abstractions;

public interface IRequestHeaderEnricher
{
    /// <summary>Applies user / correlation header forwarding. Reads <c>PermissionScope</c> in <c>Items</c> if RBAC ran first.</summary>
    void Apply(HttpContext httpContext);
}
