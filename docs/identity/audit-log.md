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
| `REGISTERED` | Register user |
| `EMAIL_CONFIRMED` | Confirm email |
| `LOGIN_SUCCESS` / `LOGIN_FAILED` | Duende login (via `Events.RaiseSuccessEvents` — TODO wire to audit) |
| `LOGOUT` | Duende endsession |
| `PASSWORD_CHANGED` | Change password |
| `PASSWORD_RESET_REQUESTED` / `PASSWORD_RESET` | Forgot/reset password |
| `PROFILE_UPDATED` | Update profile |
| `ROLE_ASSIGNED` / `ROLE_REVOKED` | Admin role management |
| `ACCOUNT_LOCKED` | Identity lockout (TODO wire) |
| `ACCOUNT_DEACTIVATED` / `ACCOUNT_ACTIVATED` | Admin |
| `EXTERNAL_LOGIN_GOOGLE` | Google OAuth |

## Query Endpoint

`GET /api/v1/identity/audit-logs?userId=&eventType=&from=&to=&pageIndex=1&pageSize=50`

Requires `Permissions.Users.Read` (scope All). Returns paginated `AuthAuditLogDto`.

## Implementation

`IIdentityAuditWriter` (in `Infrastructure/Audit/`) wraps `IAuthAuditLogRepository` and `IHttpContextAccessor` to capture IP + UA automatically. Handlers inject and call:

```csharp
await _audit.WriteAsync(userId, email, AuthEventType.RoleAssigned, new { role = "seller", by = adminId }, cancellationToken);
```

Files: [AuthAuditLog.cs](../../src/Services/Identity/UrbanX.Identity.Domain/Models/AuthAuditLog.cs), [IdentityAuditWriter.cs](../../src/Services/Identity/UrbanX.Identity.Infrastructure/Audit/IdentityAuditWriter.cs), [AuthAuditLogRepository.cs](../../src/Services/Identity/UrbanX.Identity.Persistence/Repositories/AuthAuditLogRepository.cs).
