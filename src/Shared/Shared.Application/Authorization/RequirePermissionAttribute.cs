namespace Shared.Application.Authorization;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RequirePermissionAttribute : Attribute
{
    public string Permission { get; }
    public PermissionScope MinScope { get; init; } = PermissionScope.Own;

    public RequirePermissionAttribute(string permission)
    {
        Permission = permission;
    }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RequireRoleAttribute : Attribute
{
    public string Role { get; }

    public RequireRoleAttribute(string role)
    {
        Role = role;
    }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AllowAnonymousAttribute : Attribute
{
}
