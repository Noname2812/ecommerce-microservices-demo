# Identity Service

OIDC + ASP.NET Core Identity service powered by Duende IdentityServer 7.4.6. Issues JWTs that the Gateway verifies; manages users, roles, profile, audit logs.

Port: **5005** (configured via Aspire) | DB: `urbanx_identity` | Connection string: `identitydb`

## Features (v1)

- **Core auth**: register, email confirmation, login (`/connect/token`), refresh token, logout (`/connect/endsession`)
- **Profile management**: get/update profile, change password
- **Role management** (admin): list users, assign/revoke role, deactivate/activate user
- **Google OAuth**: external login via `/api/account/external/google` → callback at `/signin-google`
- **Email confirmation + Password reset**: token-based, emails currently stubbed via `LogEmailSender` (logs to console)
- **Account lockout**: 5 failed attempts → 15-minute lockout (configurable in `Identity:Lockout`)
- **Audit log**: every auth event written to `auth_audit_logs` with user-agent + IP
- **Integration events**: `UserRegistered`, `UserProfileUpdated`, `UserRoleAssigned`, `UserRoleRevoked`, `UserDeactivated`, `UserActivated` published via Outbox

## Architecture

Standard UrbanX Clean Architecture layout:

```
Identity/
├── UrbanX.Identity.Domain/        # ApplicationUser, ApplicationRole, UserProfile, AuthAuditLog
├── UrbanX.Identity.Infrastructure/ # IEmailSender (LogEmailSender), IIdentityAuditWriter
├── UrbanX.Identity.Persistence/    # IdentityDbContext (extends OutboxDbContext) + EF configurations
├── UrbanX.Identity.Application/    # Commands, queries, validators
└── UrbanX.Identity.API/            # Carter modules + Program.cs (Duende + Identity + Google)
```

Notable: `IdentityDbContext` extends `OutboxDbContext` (not `IdentityDbContext<TUser, TRole, TKey>`) to share outbox infrastructure. ASP.NET Identity tables are configured manually via Fluent API in `Persistence/Configurations/`.

## Endpoints

### Public (whitelisted ở Gateway RBAC)
| Method | Path | Mục đích |
|---|---|---|
| POST | `/api/account/register` | Đăng ký email/password |
| POST | `/api/account/confirm-email` | Xác nhận email (body: `{ userId, token }`) |
| POST | `/api/account/forgot-password` | Gửi reset link (body: `{ email }`) |
| POST | `/api/account/reset-password` | Đặt password mới (body: `{ userId, token, newPassword }`) |
| GET | `/.well-known/openid-configuration` | OIDC discovery |
| GET | `/.well-known/openid-configuration/jwks` | Signing keys |
| POST | `/connect/token` | Login (password / refresh_token grants) |
| GET | `/connect/authorize` | Auth Code + PKCE flow |
| POST | `/connect/endsession` | Logout |
| POST | `/connect/revocation` | Revoke token |
| GET | `/connect/userinfo` | OIDC userinfo |
| GET | `/signin-google` | Google OAuth callback (set in Google Console) |

### Authenticated
| Method | Path | Permission |
|---|---|---|
| GET | `/api/v1/identity/me` | (any authenticated) |
| GET | `/api/v1/identity/profile` | (any authenticated) |
| PUT | `/api/v1/identity/profile` | (any authenticated) |
| POST | `/api/v1/identity/change-password` | (any authenticated) |
| GET | `/api/v1/identity/users` | `Permissions.Users.Read` (scope All) |
| GET | `/api/v1/identity/users/{id}` | `Permissions.Users.Read` (scope Own/All) |
| POST | `/api/v1/identity/users/{id}/roles` | `Permissions.Users.ManageRoles` |
| DELETE | `/api/v1/identity/users/{id}/roles/{role}` | `Permissions.Users.ManageRoles` |
| POST | `/api/v1/identity/users/{id}/deactivate` | `Permissions.Users.Write` (scope All) |
| POST | `/api/v1/identity/users/{id}/activate` | `Permissions.Users.Write` (scope All) |
| GET | `/api/v1/identity/roles` | `Permissions.Roles.Read` (scope All) |
| GET | `/api/v1/identity/audit-logs` | `Permissions.Users.Read` (scope All) |

## Configuration

`appsettings.json` (development):

```json
{
  "IdentityServer": { "Authority": "http://localhost:5005", "Audience": "urbanx-api" },
  "Google": { "ClientId": "", "ClientSecret": "" },
  "Identity": { "Lockout": { "MaxFailedAccessAttempts": 5, "DefaultLockoutMinutes": 15 } },
  "Seed": { "AdminEmail": "admin@urbanx.local", "AdminPassword": "Admin@123456" }
}
```

To enable Google login set `Google:ClientId` and `Google:ClientSecret` (recommended via user-secrets / env vars).

## Seed Data

On first migration, `IdentitySeeder` creates:
- Roles: `admin`, `seller`, `customer` with permission claims (`product:*`, `inventory:*`, `user:*`, `role:*` for admin)
- Default admin user: `admin@urbanx.local` / `Admin@123456` (override via `Seed:AdminEmail`/`Seed:AdminPassword`)

Permissions are stored as Role claims with type `permission`; `IUserContext.Permissions` reads them via the role claim lookup in queries.

## Token Storage Strategy

HttpOnly cookies. Frontend uses Authorization Code + PKCE flow against `/connect/authorize`. Refresh tokens are sliding 7-day. Gateway already supports reading `access_token` from cookie via `GatewayAuthenticationServiceCollectionExtensions`.

## Limitations / Out of Scope

- v1 uses `AddDeveloperSigningCredential()` — production needs cert + rotation
- `LogEmailSender` only logs to console — production needs SMTP/SendGrid
- No 2FA / MFA in v1 (ASP.NET Identity supports it, will add in v2)
- No active session management (logout-all-devices) in v1
- No mTLS between Gateway ↔ Identity (covered in `docs/auth/trust-gateway-flow.md` as production hardening)

## Initial Migration

```bash
cd src/Services/Identity/UrbanX.Identity.Persistence
dotnet ef migrations add InitialIdentitySchema
```

Migration auto-applies on Aspire startup via `Database.MigrateAsync()` in `Program.cs`.
