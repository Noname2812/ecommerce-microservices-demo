# Identity Service

OIDC + ASP.NET Core Identity service powered by Duende IdentityServer 7.4.6. Issues JWTs cho Gateway BFF + native SPA clients; manages users, roles, profile, audit logs.

Port: **5005** (pinned via Aspire) | DB: `urbanx_identity` | Connection string: `identitydb`

## Features (v1)

- **Core auth**: register, email confirmation, login (Quickstart UI tại `/Account/Login`), refresh token, logout (`/Account/Logout` + `/connect/endsession`)
- **Quickstart UI** (Razor MVC): Login form, Logout prompt, Consent screen, Error page, AccessDenied — Bootstrap 5.3 CDN, tiếng Việt. Chi tiết: `docs/identity/quickstart-ui.md`
- **Profile management**: get/update profile, change password
- **Role management** (admin): list users, assign/revoke role, deactivate/activate user
- **Google OAuth**: external login qua Quickstart UI button → `/External/Challenge?scheme=Google` → callback `/signin-google` → `/External/Callback`. Chi tiết: `docs/identity/google-oauth.md`
- **Email confirmation + Password reset**: token-based, emails currently stubbed via `LogEmailSender` (logs to console)
- **Account lockout**: 5 failed attempts → 15-minute lockout (configurable in `Identity:Lockout`)
- **Audit log**: every auth event written to `auth_audit_logs` with user-agent + IP. Chi tiết: `docs/identity/audit-log.md`
- **Integration events**: `UserRegistered`, `UserProfileUpdated`, `UserRoleAssigned`, `UserRoleRevoked`, `UserDeactivated`, `UserActivated` published via Outbox

## Architecture

Standard UrbanX Clean Architecture layout:

```
Identity/
├── UrbanX.Identity.Domain/        # ApplicationUser, ApplicationRole, UserProfile, AuthAuditLog
├── UrbanX.Identity.Infrastructure/ # IEmailSender (LogEmailSender), IIdentityAuditWriter
├── UrbanX.Identity.Persistence/    # IdentityDbContext (extends OutboxDbContext) + EF configurations
├── UrbanX.Identity.Application/    # Commands, queries, validators
└── UrbanX.Identity.API/
    ├── Apis/                      # Carter modules (AccountApis, ProfileApis, UsersApis, RolesApis, AuditApis)
    ├── Quickstart/                # MVC Controllers + ViewModels + Options (Account, External, Consent, Home)
    ├── Views/                     # Razor Views (Account/, Consent/, Home/, Shared/_Layout, ...)
    ├── wwwroot/css/site.css       # Custom Bootstrap overrides
    ├── Configuration/             # IdentityServerResources, IdentitySeeder
    └── Program.cs                 # Duende + Identity + Google + Quickstart UI MVC wiring
```

Notable: `IdentityDbContext` extends `OutboxDbContext` (not `IdentityDbContext<TUser, TRole, TKey>`) to share outbox infrastructure. ASP.NET Identity tables are configured manually via Fluent API in `Persistence/Configurations/`.

## Endpoints

### Public (whitelisted ở Gateway RBAC)

**JSON APIs (Carter modules):**
| Method | Path | Mục đích |
|---|---|---|
| POST | `/api/account/register` | Đăng ký email/password |
| POST | `/api/account/confirm-email` | Xác nhận email (body: `{ userId, token }`) |
| POST | `/api/account/forgot-password` | Gửi reset link (body: `{ email }`) |
| POST | `/api/account/reset-password` | Đặt password mới (body: `{ userId, token, newPassword }`) |

**OIDC + Quickstart UI (Duende + MVC):**
| Method | Path | Mục đích |
|---|---|---|
| GET | `/.well-known/openid-configuration` | OIDC discovery |
| GET | `/.well-known/openid-configuration/jwks` | Signing keys |
| POST | `/connect/token` | Login (authorization_code / password / refresh_token grants) |
| GET | `/connect/authorize` | Auth Code + PKCE flow |
| POST | `/connect/endsession` | Logout |
| POST | `/connect/revocation` | Revoke token |
| GET | `/connect/userinfo` | OIDC userinfo |
| GET/POST | `/Account/Login` | Quickstart UI login form |
| GET/POST | `/Account/Logout` | Quickstart UI logout |
| GET | `/Account/AccessDenied` | UI access denied |
| GET | `/External/Challenge?scheme=Google&returnUrl=...` | Trigger external auth |
| GET | `/External/Callback` | External provider callback handler |
| GET/POST | `/Consent/Index` | Quickstart UI consent screen |
| GET | `/Home/Error?errorId=...` | Duende error page |
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
  "Seed": { "AdminEmail": "admin@urbanx.local", "AdminPassword": "Admin@123456" },
  "Bff": { "BaseUrl": "http://localhost:5050", "ClientSecret": "dev-bff-secret" }
}
```

- `Bff:BaseUrl` + `Bff:ClientSecret` — Identity dùng để compose `RedirectUris`/secret cho client `urbanx-bff` ([IdentityServerResources.cs](../../src/Services/Identity/UrbanX.Identity.API/Configuration/IdentityServerResources.cs)). Phải khớp với Gateway `Bff` config.
- Bật Google login: set `Google:ClientId` và `Google:ClientSecret` (khuyến nghị qua user-secrets / env vars).

## Seed Data

On first migration, `IdentitySeeder` creates:
- Roles: `admin`, `seller`, `customer` with permission claims (`product:*`, `inventory:*`, `user:*`, `role:*` for admin)
- Default admin user: `admin@urbanx.local` / `Admin@123456` (override via `Seed:AdminEmail`/`Seed:AdminPassword`)

Permissions are stored as Role claims with type `permission`; `IUserContext.Permissions` reads them via the role claim lookup in queries.

## Token Storage Strategy

**HttpOnly cookies tại Gateway BFF**. Frontend KHÔNG bao giờ giữ access/refresh token. Flow:

1. Browser → Gateway `/bff/login` → 302 → Identity `/connect/authorize` (Auth Code + PKCE)
2. Identity issues login UI → user authenticates → 302 back → Gateway `/signin-oidc?code=...`
3. Gateway exchange code (server-to-server, dùng client secret của `urbanx-bff` confidential client) → access_token + refresh_token + id_token
4. Gateway lưu tokens vào server-side session (in-memory cho dev, Redis/SQL cho prod), set cookie `urbanx.bff` HttpOnly
5. Browser tiếp theo gửi cookie → Gateway resolve session → attach Authorization Bearer khi forward downstream (BFF.Yarp transform), nhưng RequestHeaderEnricher strip cả Cookie + Authorization và set X-User-* headers (Trust-the-Gateway)

Refresh tokens sliding 7-day, one-time-use. SPA chỉ dùng `/bff/user`, `/bff/login`, `/bff/logout` — KHÔNG gọi trực tiếp `/connect/*`.

Chi tiết: `docs/gateway/bff.md`.

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
