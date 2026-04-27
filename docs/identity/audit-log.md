# Auth Audit Log

Every authentication / authorization event is recorded in the `auth_audit_logs` table.

## Schema

| Column | Type | Notes |
|---|---|---|
| `id` | uuid (PK) | |
| `user_id` | uuid? | Nullable for anonymous events (e.g., failed login by unknown email) |
| `email` | varchar(256)? | Captured for traceability |
| `event_type` | varchar(50) | See `AuthEventType` constants |
| `ip_address` | varchar(64)? | From `HttpContext.Connection.RemoteIpAddress` |
| `user_agent` | varchar(512)? | From `User-Agent` header |
| `metadata_json` | jsonb? | Free-form per event (e.g., role name, by user id) |
| `occurred_at` | timestamptz | UTC |

Indexes: `user_id`, `event_type`, `occurred_at` (for filter + sort).

## Event Types

| Constant | Recorded by |
|---|---|
| `REGISTERED` | `RegisterUserCommandHandler` |
| `EMAIL_CONFIRMED` | `ConfirmEmailCommandHandler` |
| `LOGIN_SUCCESS` | `AccountController.Login` (Quickstart UI) — sau khi `SignInManager.PasswordSignInAsync` thành công |
| `LOGIN_FAILED` | `AccountController.Login` — metadata `reason=invalid_credentials` hoặc `reason=inactive` |
| `LOGOUT` | `AccountController.Logout` (Quickstart UI) — sau `SignInManager.SignOutAsync` |
| `PASSWORD_CHANGED` | `ChangePasswordCommandHandler` |
| `PASSWORD_RESET_REQUESTED` / `PASSWORD_RESET` | Forgot/Reset password handlers |
| `PROFILE_UPDATED` | `UpdateProfileCommandHandler` |
| `ROLE_ASSIGNED` / `ROLE_REVOKED` | `AssignRole`/`RevokeRole` handlers |
| `ACCOUNT_LOCKED` | `AccountController.Login` — khi `result.IsLockedOut == true` từ `PasswordSignInAsync` |
| `ACCOUNT_DEACTIVATED` / `ACCOUNT_ACTIVATED` | `Deactivate`/`Activate` handlers |
| `EXTERNAL_LOGIN_GOOGLE` | `ExternalController.Callback` — metadata `provider=Google` |

## Query Endpoint

`GET /api/v1/identity/audit-logs?userId=&eventType=&from=&to=&pageIndex=1&pageSize=50`

Requires `Permissions.Users.Read` (scope All). Returns paginated `AuthAuditLogDto`.

## Implementation

`IIdentityAuditWriter` (in `Infrastructure/Audit/`) wraps `IAuthAuditLogRepository` and `IHttpContextAccessor` to capture IP + UA automatically. Handlers inject and call:

```csharp
await _audit.WriteAsync(userId, email, AuthEventType.RoleAssigned, new { role = "seller", by = adminId }, cancellationToken);
```

Files: [AuthAuditLog.cs](../../src/Services/Identity/UrbanX.Identity.Domain/Models/AuthAuditLog.cs), [IdentityAuditWriter.cs](../../src/Services/Identity/UrbanX.Identity.Infrastructure/Audit/IdentityAuditWriter.cs), [AuthAuditLogRepository.cs](../../src/Services/Identity/UrbanX.Identity.Persistence/Repositories/AuthAuditLogRepository.cs).
