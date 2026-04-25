# Auth Flow: Trust-the-Gateway

## Mục đích

Tách trách nhiệm:
- **Gateway** = authentication (verify JWT, ai là ai)
- **Service** = authorization (làm được gì với resource)

Gateway verify JWT một lần, enrich identity vào headers, strip Authorization header, rồi forward sang service. Service không decode JWT — chỉ đọc headers qua `IUserContext` rồi check permission qua `AuthorizationPipelineBehavior` (MediatR).

## Sơ đồ

```
Client ──Authorization: Bearer <jwt>──▶ Gateway
                                          ├─ JwtBearer verify
                                          ├─ GatewayRbacMiddleware (coarse RBAC)
                                          ├─ RequestHeaderEnricher set X-User-*
                                          └─ strip Authorization + Cookie
                                                  │
                                                  ▼ YARP forward
                                          Catalog/Inventory Service
                                          ├─ UserContextMiddleware (tracing tags)
                                          ├─ Carter endpoint (no RequireAuthorization)
                                          ▼ MediatR.Send(command)
                                          ├─ LoggingPipelineBehavior
                                          ├─ IdempotencyPipelineBehavior
                                          ├─ AuthorizationPipelineBehavior
                                          │     ├─ reflect [RequirePermission] / [RequireRole] / [AllowAnonymous]
                                          │     └─ check IUserContext (UserId, Roles, Scope)
                                          ├─ ValidationPipelineBehavior
                                          ├─ TransactionPipelineBehavior
                                          └─ Handler (inject IUserContext khi cần UserId)
```

## Headers Gateway → Service

Định nghĩa tại [`GatewayHeaderNames.cs`](../../src/Shared/Shared.Kernel/Constants/GatewayHeaderNames.cs):

| Header | Nguồn | Ví dụ |
|---|---|---|
| `X-User-Id` | claim `sub` | `e35c3863-485f-47bc-98b4-2ebdfdb4a2cb` |
| `X-User-Roles` | claim `role`/`roles` (comma-separated) | `seller,merchant` |
| `X-Merchant-Id` | claim `merchant_id` | guid |
| `X-Permission-Scope` | RBAC middleware (`own` hoặc `all`) | `own` |
| `X-Request-Id` | correlation id | guid |

Gateway **strip** `Authorization` và `Cookie` trước khi forward — service không bao giờ thấy JWT.

## API permission ở Service

Định nghĩa permissions/roles bằng constants (không dùng string literal):

```csharp
// src/Shared/Shared.Application/Authorization/Permissions.cs
public static class Permissions
{
    public static class Products
    {
        public const string Read  = "product:read";
        public const string Write = "product:write";
    }
    public static class Inventory
    {
        public const string Read  = "inventory:read";
        public const string Write = "inventory:write";
    }
}

public static class Roles
{
    public const string Admin = "admin";
    // ...
}
```

Gắn attribute trên Command/Query class:

```csharp
[RequirePermission(Permissions.Products.Write)]
public record CreateProductCommand(...) : ICommand<Guid>;

[RequirePermission(Permissions.Products.Write, MinScope = PermissionScope.Own)]
public record UpdateProductBasicInfoCommand(...) : ICommand;

[RequirePermission(Permissions.Products.Read, MinScope = PermissionScope.Own)]
public record GetVariantDeleteEligibilityQuery(...) : IQuery<...>;

[AllowAnonymous]
public record PublicHealthCheckQuery() : IQuery<...>;
```

**Quy ước:**
- Không gắn attribute → mặc định request là anonymous (behavior bỏ qua check). Dùng `[AllowAnonymous]` để declare ý định rõ ràng.
- `MinScope = PermissionScope.Own` → caller phải có Scope `Own` hoặc `All`. Handler tự check ownership (`product.SellerId == _user.UserId`).
- `MinScope = PermissionScope.All` → chỉ admin/all-scope mới qua được.

## IUserContext trong Handler

```csharp
public sealed class UpdateProductBasicInfoCommandHandler : ICommandHandler<UpdateProductBasicInfoCommand>
{
    private readonly IUserContext _userContext;
    // ... ctor injects IUserContext

    public async Task<Result> Handle(UpdateProductBasicInfoCommand request, CancellationToken ct)
    {
        var product = await _productRepository.GetByIdForUpdateAsync(request.ProductId, ct);
        if (product is null) return Result.Failure(CatalogErrors.ProductNotFound(request.ProductId));

        // Ownership check khi scope=Own
        if (_userContext.Scope == PermissionScope.Own && product.SellerId != _userContext.UserId)
            return Result.Failure(CatalogErrors.Forbidden());

        // ... business logic
    }
}
```

Behavior đã đảm bảo `_userContext.IsAuthenticated == true` trước khi handler chạy (nếu command có attribute `[RequirePermission]`).

## Failure → HTTP mapping

| Error code | HTTP |
|---|---|
| `AUTH_REQUIRED` | 401 |
| `FORBIDDEN` | 403 |

Mapping ở [`ApiEndpoint.cs`](../../src/Services/Catalog/UrbanX.Catalog.API/Abstractions/ApiEndpoint.cs).

## Cấu trúc file

| File | Mục đích |
|---|---|
| `Shared.Application/Authorization/IUserContext.cs` | Interface đọc identity |
| `Shared.Application/Authorization/PermissionScope.cs` | enum None/Own/All |
| `Shared.Application/Authorization/Permissions.cs` | Constants `Permissions.*` + `Roles.*` |
| `Shared.Application/Authorization/RequirePermissionAttribute.cs` | Attributes `RequirePermission`, `RequireRole`, `AllowAnonymous` |
| `Shared.Kernel/Primitives/AuthorizationErrors.cs` | `AUTH_REQUIRED`, `FORBIDDEN` errors |
| `Shared.Messaging/Authorization/UserHttpContext.cs` | impl `IUserContext` đọc từ header (internal) |
| `Shared.Messaging/Authorization/UserContextMiddleware.cs` | Set OpenTelemetry activity tags |
| `Shared.Messaging/Authorization/UserContextApplicationBuilderExtensions.cs` | `app.UseUserContext()` |
| `Shared.Messaging/Behaviors/AuthorizationPipelineBehavior.cs` | MediatR behavior reflect attribute → check `IUserContext` |

## Đăng ký vào service

`AddMediator()` trong `Shared.Messaging` đã tự động:
- `AddHttpContextAccessor()`
- `AddScoped<IUserContext, UserHttpContext>()`
- Đăng ký `AuthorizationPipelineBehavior`

Trong `Program.cs` của mỗi service, gọi `app.UseUserContext()` trước `app.MapCarter()`. Đã làm cho Catalog + Inventory.

**Bỏ:** `AddAuthentication(JwtBearer)`, `app.UseAuthentication()`, `app.UseAuthorization()`, `.RequireAuthorization()` ở endpoints.

## ⚠️ Trust boundary — production caveat

Pattern này **tin tưởng Gateway 100%**. Nếu attacker reach service trực tiếp (bypass Gateway), họ tự set `X-User-Id: <admin-uuid>` và bypass auth.

**Bắt buộc cho production** (ngoài scope plan này):
1. **mTLS** giữa Gateway ↔ Service — service verify client certificate là Gateway
2. **HOẶC** shared-secret header (HMAC) — service verify chữ ký
3. **HOẶC** network isolation — service chỉ accessible từ private subnet/cluster, không expose public

Hiện UrbanX deploy local/Aspire — services chỉ accessible qua Aspire networking, chấp nhận được cho dev. Trước khi deploy thật, làm 1 trong các option trên.

## Service-to-service / messaging (chưa làm)

Khi Catalog gọi Inventory qua HTTP hay khi consumer xử lý integration event — cần propagate `X-User-*` headers. Plan riêng:
- HTTP: `DelegatingHandler` đọc current `IUserContext` → set headers vào outgoing request
- MassTransit: header propagation qua message context

## Test

- Unit tests mock `IUserContext`:
  ```csharp
  var userContext = new Mock<IUserContext>();
  userContext.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
  userContext.SetupGet(u => u.IsAuthenticated).Returns(true);
  userContext.SetupGet(u => u.Scope).Returns(PermissionScope.All);
  ```
- Integration: gửi request kèm header `X-User-Id` → service xử lý OK.
- Bỏ header → `AuthorizationPipelineBehavior` trả `AUTH_REQUIRED` → 401.
