# Identity Service

.NET 10 — Clean Architecture, Carter, MediatR (CQRS), EF Core + PostgreSQL, Transactional Outbox, **Duende IdentityServer 7.4.6 + ASP.NET Core Identity 10.0.5**.

Port: **5005** | DB: `urbanx_identity` | Connection string: `identitydb` | Status: **Active**

Issuer JWT cho toàn hệ thống. Service KHÁC tin tưởng JWT do service này phát hành (Trust-the-Gateway → Identity → mọi service khác).

---

## Projects

| Project | Responsibility |
|---|---|
| `UrbanX.Identity.Domain` | Entities (`ApplicationUser`, `ApplicationRole`, `UserProfile`, `AuthAuditLog`), `AuthEventType` constants, `IAuthAuditLogRepository` |
| `UrbanX.Identity.Infrastructure` | `IEmailSender` + `LogEmailSender`, `IIdentityAuditWriter` + `IdentityAuditWriter` (capture IP/UA via `IHttpContextAccessor`) |
| `UrbanX.Identity.Persistence` | `IdentityDbContext` (extends `OutboxDbContext`), EF configurations cho Identity tables + outbox + custom tables, `AuthAuditLogRepository` |
| `UrbanX.Identity.Application` | Commands, queries, validators, `AuthErrors` |
| `UrbanX.Identity.API` | Carter modules, `IdentityServer` config, `IdentitySeeder`, Program.cs |

**Dependency order:** Domain ← Infrastructure / Persistence ← Application ← API

> Lưu ý đặc biệt: Application reference Infrastructure (khác Inventory) vì cần `IEmailSender` + `IIdentityAuditWriter` từ Infrastructure.

---

## Domain

### Entities

**ApplicationUser** — kế thừa `IdentityUser<Guid>`
- Thêm: `DisplayName`, `AvatarUrl?`, `MerchantId?`, `IsActive` (default true), `CreatedAt`, `UpdatedAt`, `DeactivatedAt?`, `DeactivationReason?`
- Navigation: `UserProfile? Profile`

**ApplicationRole** — kế thừa `IdentityRole<Guid>`
- Thêm: `Description?`, `CreatedAt`

**UserProfile** — extra profile data, FK 1-1 với `ApplicationUser`
- `UserId` (PK), `Bio?`, `DateOfBirth?`, `Gender?`, `AddressLine?`, `City?`, `Country?`, `PostalCode?`, `UpdatedAt`

**AuthAuditLog** — append-only audit trail (kế thừa `BaseEntity<Guid>`)
- `UserId?`, `Email?`, `EventType` (xem `AuthEventType`), `IpAddress?`, `UserAgent?`, `MetadataJson?` (jsonb), `OccurredAt`
- Indexes: `UserId`, `EventType`, `OccurredAt`

### Value Objects

`AuthEventType` (`static class`, UPPER_CASE constants): `REGISTERED`, `EMAIL_CONFIRMED`, `LOGIN_SUCCESS`, `LOGIN_FAILED`, `LOGOUT`, `PASSWORD_CHANGED`, `PASSWORD_RESET_REQUESTED`, `PASSWORD_RESET`, `PROFILE_UPDATED`, `ROLE_ASSIGNED`, `ROLE_REVOKED`, `ACCOUNT_LOCKED`, `ACCOUNT_DEACTIVATED`, `ACCOUNT_ACTIVATED`, `EXTERNAL_LOGIN_GOOGLE`.

### Repository Interfaces

`IAuthAuditLogRepository` — `AddAsync`, `ListAsync(userId, eventType, from, to, page, size)`.

User/Role CRUD đi qua ASP.NET Identity `UserManager<ApplicationUser>` / `RoleManager<ApplicationRole>` thay vì repository thuần.

---

## Application

### MediatR Behavior

`TransactionPipelineBehavior` (từ `Shared.Messaging`) — wraps mọi command trong DB transaction qua `IUnitOfWork` (impl: `EfUnitOfWork` trong Persistence, đảm bảo Outbox + business write atomic). Behaviors registered mặc định bởi `AddMediatorWithPielineDefault`: Authorization → Idempotency → Validation → DistributedLock → Transaction.

### Commands (Usecases/V1/Command/)

| Command | Permission | Phát events / Audit |
|---|---|---|
| `RegisterUser` | `[AllowAnonymous]` | `UserRegisteredV1` + audit `REGISTERED`; assign role `customer`; gửi email confirm link |
| `ConfirmEmail` | `[AllowAnonymous]` | audit `EMAIL_CONFIRMED` |
| `ForgotPassword` | `[AllowAnonymous]` | audit `PASSWORD_RESET_REQUESTED`; gửi email reset link (không leak email tồn tại) |
| `ResetPassword` | `[AllowAnonymous]` | audit `PASSWORD_RESET` |
| `ChangePassword` | (authenticated) | audit `PASSWORD_CHANGED` |
| `UpdateProfile` | (authenticated) | `UserProfileUpdatedV1` + audit `PROFILE_UPDATED`; tự tạo `UserProfile` nếu chưa có |
| `AssignRole` | `Permissions.Users.ManageRoles` (scope All) | `UserRoleAssignedV1` + audit `ROLE_ASSIGNED`; chặn user tự assign role mình |
| `RevokeRole` | `Permissions.Users.ManageRoles` (scope All) | `UserRoleRevokedV1` + audit `ROLE_REVOKED` |
| `DeactivateUser` | `Permissions.Users.Write` (scope All) | `UserDeactivatedV1` + audit `ACCOUNT_DEACTIVATED` |
| `ActivateUser` | `Permissions.Users.Write` (scope All) | `UserActivatedV1` + audit `ACCOUNT_ACTIVATED` |

### Queries (Usecases/V1/Query/)

| Query | Permission | Trả về |
|---|---|---|
| `GetCurrentUser` | (authenticated) | `UserProfileDto` (current user, full profile + roles + permissions) |
| `GetUserById` | `Permissions.Users.Read` (scope Own/All) | `UserProfileDto` (own scope chỉ xem được chính mình) |
| `ListUsers` | `Permissions.Users.Read` (scope All) | `PageResult<UserSummaryDto>` (filter: searchTerm, role, isActive) |
| `ListAllRoles` | `Permissions.Roles.Read` (scope All) | `IReadOnlyList<RoleDto>` |
| `ListAuditLogs` | `Permissions.Users.Read` (scope All) | `PageResult<AuthAuditLogDto>` (filter: userId, eventType, from, to) |

### Errors (`AuthErrors.cs`)

```csharp
AuthErrors.InvalidCredentials, EmailNotConfirmed, AccountLocked, AccountDeactivated
AuthErrors.EmailAlreadyExists(email), InvalidConfirmationToken, InvalidPasswordResetToken
AuthErrors.WeakPassword(detail), CurrentPasswordIncorrect
AuthErrors.UserNotFound(id), UserNotFoundByEmail(email)
AuthErrors.RoleNotFound(role), UserAlreadyInRole(role), UserNotInRole(role)
AuthErrors.CannotChangeOwnRole
```

---

## Persistence

### `IdentityDbContext`

**Quan trọng:** Kế thừa `OutboxDbContext` (KHÔNG phải `IdentityDbContext<TUser, TRole, TKey>` của EF). Lý do: cần share outbox infrastructure với pattern còn lại của UrbanX. Identity tables được wire-up thủ công qua DbSets + EF Configurations:

```csharp
public DbSet<ApplicationUser> Users
public DbSet<ApplicationRole> Roles
public DbSet<IdentityUserRole<Guid>> UserRoles
public DbSet<IdentityUserClaim<Guid>> UserClaims
public DbSet<IdentityUserLogin<Guid>> UserLogins
public DbSet<IdentityUserToken<Guid>> UserTokens
public DbSet<IdentityRoleClaim<Guid>> RoleClaims
public DbSet<UserProfile> UserProfiles
public DbSet<AuthAuditLog> AuthAuditLogs
// + OutboxMessages (inherited from OutboxDbContext)
```

`AddEntityFrameworkStores<IdentityDbContext>()` của ASP.NET Identity tìm các DbSet này qua reflection — không cần inherit `IdentityDbContext` của EF.

### Table Names (snake_case)

| Entity | Table |
|---|---|
| ApplicationUser | `users` |
| ApplicationRole | `roles` |
| IdentityUserRole | `user_roles` |
| IdentityUserClaim | `user_claims` |
| IdentityUserLogin | `user_logins` |
| IdentityUserToken | `user_tokens` |
| IdentityRoleClaim | `role_claims` |
| UserProfile | `user_profiles` |
| AuthAuditLog | `auth_audit_logs` |
| OutboxMessage | `outbox_messages` (inherited) |

### Notable EF Config

- `ApplicationUser`: unique index `normalized_username`, index `normalized_email`, `IsActive` default true, `ConcurrencyStamp` concurrency token, 1-1 navigation `Profile` cascade delete
- `ApplicationRole`: unique index `normalized_name`, `ConcurrencyStamp` concurrency token
- `AuthAuditLog`: `metadata_json` jsonb column; indexes trên `user_id`, `event_type`, `occurred_at`
- Tất cả PKs: `ValueGeneratedNever()` (app assigns GUID)

### Migrations

Pending: `InitialIdentitySchema` (chưa chạy)

```bash
cd src/Services/Identity/UrbanX.Identity.Persistence
dotnet ef migrations add InitialIdentitySchema
```

Migration apply tự động khi khởi động qua `Database.MigrateAsync()` trong Program.cs. Sau migration, `IdentitySeeder` chạy:
1. Tạo 3 roles: `admin`, `seller`, `customer` + permission claims (claim type `permission`)
2. Tạo admin user `admin@urbanx.local` / `Admin@123456` (override qua `Seed:AdminEmail/Password`)

### Design-Time Factory

`IdentityDbContextFactory` đọc env var `ConnectionStrings__identitydb`.
Fallback: `Host=localhost;Port=5432;Database=urbanx_identity;Username=postgres;Password=postgres`

---

## API

### Carter Modules

| Module | Base path | Endpoints |
|---|---|---|
| `AccountApis` | `/api/account` | POST `register`, `confirm-email`, `forgot-password`, `reset-password` (all `[AllowAnonymous]`) |
| `ProfileApis` | `/api/v1/identity` | GET `me` / `profile`, PUT `profile`, POST `change-password` |
| `UsersApis` | `/api/v1/identity/users` | GET list / by id, POST `{id}/roles`, DELETE `{id}/roles/{role}`, POST `{id}/{deactivate,activate}` |
| `RolesApis` | `/api/v1/identity/roles` | GET list |
| `AuditApis` | `/api/v1/identity/audit-logs` | GET list (filter: userId, eventType, from, to) |

OIDC endpoints (Duende, không dùng Carter): `/connect/{token,authorize,endsession,userinfo,revocation}`, `/.well-known/openid-configuration`, `/signin-google`.

### `ApiEndpoint` base class

`ToIdentityResult(Result)` / `ToIdentityResult<T>(Result<T>)` — maps error codes → HTTP status:
- `AUTH_REQUIRED` → 401
- `FORBIDDEN` → 403
- `*NotFound` → 404
- `Auth.EmailAlreadyExists`, `User.AlreadyInRole` → 409
- default → 400

### Authentication / Authorization

**Identity là exception trong Trust-the-Gateway pattern** — nó là JWT issuer, KHÔNG đọc `X-User-*` headers cho `/connect/*` (Duende tự handle authentication qua cookie/external). Tuy nhiên các management endpoint `/api/v1/identity/**` đi qua Gateway và **vẫn dùng `IUserContext`** đọc headers (giống service khác).

- `app.UseUserContext()` — dùng cho Carter endpoints (đọc `X-User-*` headers từ Gateway)
- `app.UseIdentityServer()` — Duende OIDC pipeline cho `/connect/*` và `/.well-known/*`
- Command/Query gắn `[RequirePermission(Permissions.Users.*)]` hoặc `[AllowAnonymous]`
- `AuthorizationPipelineBehavior` xử lý attribute reflection (giống Catalog/Inventory)

### Duende Configuration (`Configuration/IdentityServerResources.cs`)

In-memory clients (dev):
- `urbanx-spa` — Authorization Code + PKCE, allows offline_access, sliding refresh token 7 ngày, redirect `http://localhost:5173/auth/callback`, CORS `http://localhost:5173`
- `urbanx-test-password` — Resource Owner Password (dev only), client secret `dev-secret`, dùng cho integration test

Identity resources: `openid`, `profile`, `email`, `roles` (claim `role`), `merchant` (claim `merchant_id`)
API scope: `urbanx-api` với UserClaims `role`, `merchant_id`, `email`

Production hardening (chưa làm v1):
- Thay `AddDeveloperSigningCredential()` bằng cert thật + key rotation
- Migrate clients/resources từ in-memory sang `Duende.IdentityServer.EntityFramework` (package đã pin)

### Program.cs Registration Order

```
AddServiceDefaults() → AddOpenApi() → AddNpgsqlDbContext<IdentityDbContext>("identitydb")
→ AddOutbox<IdentityDbContext>() → AddConfigMessaging() → AddMessaging()
→ AddIdentity<ApplicationUser, ApplicationRole>().AddEntityFrameworkStores().AddDefaultTokenProviders()
→ ConfigureApplicationCookie() → AddIdentityServer().AddInMemory*().AddAspNetIdentity()
→ AddDeveloperSigningCredential() (dev)
→ AddAuthentication().AddGoogle() (only if Google:ClientId/Secret set)
→ AddApplication() → Carter

app.UseExceptionHandler() → app.UseUserContext() → app.UseRouting()
→ app.UseIdentityServer() → app.UseAuthorization()
→ migrate + IdentitySeeder.SeedAsync()
→ app.MapCarter()
```

### Account Lockout

Cấu hình trong `Identity:Lockout`:
- `MaxFailedAccessAttempts` (default 5)
- `DefaultLockoutMinutes` (default 15)

Khi user đạt max → `LockoutEnd` set → các grant của Duende fail với `invalid_grant`. (TODO: wire `IdentityServerEvents.UserLoginFailedEvent` vào `IIdentityAuditWriter` để emit `ACCOUNT_LOCKED` event tự động.)

---

## Gateway Routes

Đã thêm sẵn trong `src/Gateway/UrbanX.Gateway/appsettings.json`:

| Route ID | Path | Public? |
|---|---|---|
| `identity-account-route` | `/api/account/{**catch-all}` | partial — register, confirm-email, forgot-password, reset-password public; /external GET public |
| `identity-connect-route` | `/connect/{**catch-all}` | token, authorize, endsession, userinfo, revocation, introspect public |
| `identity-wellknown-route` | `/.well-known/{**catch-all}` | public |
| `identity-signin-google-route` | `/signin-google` | public |
| `identity-api-route` | `/api/v1/identity/{**catch-all}` | authenticated |

Whitelist trong `GatewayRbacOptionsSetup.cs` đã được cập nhật.

---

## AppHost Registration

```csharp
var identityDb = postgres.AddDatabase("identitydb", "urbanx_identity");

var identityService = builder.AddProject<Projects.UrbanX_Identity_API>("identity")
    .WithReference(identityDb)
    .WithReference(rabbitMq)
    .WaitFor(identityDb)
    .WaitFor(rabbitMq);

// Mọi downstream service WithReference(identityService) để Aspire inject env services__identity__*
catalogService.WithReference(identityService).WaitFor(identityService);
inventoryService.WithReference(identityService).WaitFor(identityService);
gateway.WithReference(identityService).WaitFor(identityService);
```

---

## Integration Events

Defined trong `src/Shared/Shared.Contract/Messaging/Identity/UserIntegrationEvents.cs`:

- `UserRegisteredV1(UserId, Email, DisplayName, PhoneNumber?, MerchantId?, Roles)`
- `UserProfileUpdatedV1(UserId, Email, DisplayName, PhoneNumber?, AvatarUrl?)`
- `UserRoleAssignedV1(UserId, Email, Role, AssignedBy?)`
- `UserRoleRevokedV1(UserId, Email, Role, RevokedBy?)`
- `UserDeactivatedV1(UserId, Email, DeactivatedBy?, Reason?)`
- `UserActivatedV1(UserId, Email, ActivatedBy?)`

Tất cả phát qua `IOutboxWriter` trong handler → `OutboxRelayWorker` publish lên RabbitMQ. Consumer ở các service khác (Catalog, Inventory) cần kế thừa `IntegrationEventConsumerBase<TEvent, TConsumer>`.

---

## Configuration

`appsettings.json`:

```json
{
  "IdentityServer": { "Authority": "http://localhost:5005", "Audience": "urbanx-api" },
  "Google": { "ClientId": "", "ClientSecret": "" },
  "Identity": { "Lockout": { "MaxFailedAccessAttempts": 5, "DefaultLockoutMinutes": 15 } },
  "Seed": { "AdminEmail": "admin@urbanx.local", "AdminPassword": "Admin@123456" },
  "RabbitMq": { "Host": "localhost", "Username": "guest", "Password": "guest" }
}
```

Bật Google OAuth: set qua user-secrets/env vars (KHÔNG commit ClientSecret):

```bash
cd src/Services/Identity/UrbanX.Identity.API
dotnet user-secrets set "Google:ClientId" "<...>.apps.googleusercontent.com"
dotnet user-secrets set "Google:ClientSecret" "<secret>"
```

Nếu một trong 2 giá trị rỗng, Google handler **không được register** (Program.cs guard).

---

## Key Patterns

**Trust-the-Gateway** — Identity là special case: tự xác thực user qua password/Google ở `/connect/*`, nhưng các management endpoints `/api/v1/identity/**` vẫn đọc identity từ `X-User-*` headers (qua Gateway).

**Transactional Outbox** — Mọi command commit business data + integration event trong cùng 1 transaction. `TransactionPipelineBehavior` (qua `IUnitOfWork`) wrap, `IOutboxWriter` ghi outbox, `OutboxRelayWorker` publish.

**Permission Claims** — Permissions là role claims (claim type `permission`). `IdentitySeeder` populate; `GetCurrentUserQueryHandler` aggregate lên `UserProfileDto.Permissions`. Gateway RBAC tự đọc qua `PermissionClaimReader`.

**Audit Capture** — `IIdentityAuditWriter.WriteAsync(...)` tự capture IP + UA từ `IHttpContextAccessor`, không cần handler quan tâm. Inject vào mọi handler có audit-worthy event.
