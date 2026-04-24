namespace UrbanX.Gateway.Application.Configuration;

public enum EndpointAccessKind
{
    /// <summary>No token required; RBAC and permission headers are not applied.</summary>
    Public,

    /// <summary>Valid JWT required; no specific permission claim check.</summary>
    Authenticated,

    /// <summary>Valid JWT and at least one of the permission claims (or wildcard) is required.</summary>
    Permission
}
