namespace Shared.Kernel.Primitives;

public static class AuthorizationErrors
{
    public static readonly Error Unauthenticated = new("AUTH_REQUIRED", "Authentication required");

    public static Error MissingPermission(string permission) =>
        new("FORBIDDEN", $"Missing permission: {permission}");

    public static Error MissingRole(string role) =>
        new("FORBIDDEN", $"Missing role: {role}");

    public static Error InsufficientScope(string permission) =>
        new("FORBIDDEN", $"Insufficient scope for permission: {permission}");
}
