# Roles & Permissions

## Roles (seeded)

| Role | Description | Permission claims |
|---|---|---|
| `admin` | Administrator | `product:read/write`, `inventory:read/write`, `user:read/write`, `user:manage-roles`, `role:read/write` |
| `seller` | Merchant seller | `product:read/write`, `inventory:read/write`, `user:read` |
| `customer` | End customer (default on register) | `product:read`, `user:read` |

## Permission Constants

Defined in `src/Shared/Shared.Application/Authorization/Permissions.cs`:

```csharp
public static class Permissions
{
    public static class Products  { Read, Write }
    public static class Inventory { Read, Write }
    public static class Users     { Read, Write, ManageRoles }
    public static class Roles     { Read, Write }
}
```

## How Permission Claims Are Stored

Each role has claims of type `permission` (one row per permission in the `role_claims` table). `IdentitySeeder` creates them on first startup. Look up via:

```csharp
var role = await roleManager.FindByNameAsync(roleName);
var permissions = (await roleManager.GetClaimsAsync(role))
    .Where(c => c.Type == "permission")
    .Select(c => c.Value);
```

## Endpoints

### List All Roles
`GET /api/v1/identity/roles` — requires `Permissions.Roles.Read` (scope All).

### Assign Role
`POST /api/v1/identity/users/{id}/roles`

```json
{ "role": "seller" }
```

Requires `Permissions.Users.ManageRoles` (scope All). User cannot change own role assignments — returns `User.CannotChangeOwnRole`. Conflicts: `User.AlreadyInRole` → 409, `Role.NotFound` → 404, `User.NotFound` → 404.

Audit: `ROLE_ASSIGNED` · Outbox event: `UserRoleAssignedV1`.

### Revoke Role
`DELETE /api/v1/identity/users/{id}/roles/{role}` — same permission. Errors: `User.NotInRole` (400), `User.NotFound` (404).

Audit: `ROLE_REVOKED` · Outbox event: `UserRoleRevokedV1`.

### List User Permissions
`GET /api/v1/identity/users/{id}` returns user with aggregated `permissions` array (union from all assigned roles).

## Authorization Enforcement

Identity service uses the same Trust-the-Gateway pattern: command/query classes are decorated with `[RequirePermission(Permissions.X.Y, MinScope = ...)]`. The `AuthorizationPipelineBehavior` inspects these attributes and short-circuits with `AuthorizationErrors.MissingPermission` if the user's `IUserContext.Scope` is insufficient.

Files: [Permissions.cs](../../src/Shared/Shared.Application/Authorization/Permissions.cs), [IdentitySeeder.cs](../../src/Services/Identity/UrbanX.Identity.API/Configuration/IdentitySeeder.cs), [AssignRoleCommand.cs](../../src/Services/Identity/UrbanX.Identity.Application/Usecases/V1/Command/AssignRole/AssignRoleCommand.cs).
