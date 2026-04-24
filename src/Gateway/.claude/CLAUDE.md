# Gateway Service — UrbanX

YARP reverse proxy, JWT authentication, RBAC, rate limiting, header enrichment.

---

## Project Layout

```
src/Gateway/
├── UrbanX.Gateway/                   # Entry point (Program.cs)
├── UrbanX.Gateway.Application/       # Abstractions, config options, constants
└── UrbanX.Gateway.Infrastructure/    # Middleware implementations, DI extensions
```

### UrbanX.Gateway.Application

| Path | Purpose |
|---|---|
| `Abstractions/IEndpointAccessRegistry.cs` | RBAC: maps (method, path) → `EndpointAccessResult` |
| `Abstractions/IGatewayReverseProxy.cs` | Abstraction over YARP registration |
| `Abstractions/IKestrelEdgeTlsConfiguration.cs` | Optional in-process TLS setup |
| `Abstractions/IRequestHeaderEnricher.cs` | Enriches downstream request headers |
| `Configuration/CorsEdgeOptions.cs` | CORS policy config — section `"Cors"` |
| `Configuration/EdgeCorsPolicyNames.cs` | Constant `"UrbanX.EdgeCors"` |
| `Configuration/EndpointAccessKind.cs` | Enum: `Public`, `Authenticated`, `Permission` |
| `Configuration/EndpointAccessResult.cs` | Result returned by `IEndpointAccessRegistry` |
| `Configuration/GatewayJwtOptions.cs` | JWT authority/audience — section `"Jwt"` |
| `Configuration/GatewayRbacOptions.cs` | RBAC rules — section `"GatewayRbac"` |
| `Configuration/KestrelEdgeOptions.cs` | Edge TLS (PFX) — section `"Kestrel:Edge"` |
| `Configuration/ProtectedRouteEntry.cs` | Route needing JWT ± permissions |
| `Configuration/PublicRouteEntry.cs` | Route that bypasses auth |
| `Configuration/RateLimitingOptions.cs` | Sliding-window limits — section `"RateLimit"` |
| `Constants/GatewayContextItems.cs` | `HttpContext.Items` keys (e.g. `PermissionScope`) |
| `Constants/GatewayErrorCodes.cs` | JSON `error` field values (e.g. `UNAUTHORIZED`) |
| `Constants/GatewayHeaderNames.cs` | All header name strings (e.g. `X-User-Id`) |

### UrbanX.Gateway.Infrastructure

| Path | Purpose |
|---|---|
| `Correlation/GatewayRequestCorrelationMiddleware.cs` | Assigns `X-Request-Id` if absent |
| `DependencyInjection/GatewayAuthenticationServiceCollectionExtensions.cs` | JWT Bearer + `OnChallenge` 401 handler |
| `DependencyInjection/GatewayCorsServiceCollectionExtensions.cs` | `AddGatewayCors()` — registers `CorsEdgeOptions` + policy |
| `DependencyInjection/GatewayInfrastructureServiceCollectionExtensions.cs` | **Master DI entry point** `AddGatewayInfrastructure()` and pipeline helpers `UseGatewayEdgeCors()`, `MapGatewayReverseProxy()` |
| `DependencyInjection/GatewayRateLimitingServiceCollectionExtensions.cs` | `AddGatewayRateLimiting()` — 5 sliding-window partitions |
| `DependencyInjection/GatewayRequestPipelineExtensions.cs` | `UseGatewayDownstreamPipeline()` — orders all middleware |
| `Edge/KestrelEdgeTlsConfiguration.cs` | Loads PFX cert and calls `kestrel.ListenAnyIP/Listen` |
| `Enrichment/GatewayRequestEnrichmentMiddleware.cs` | Thin middleware that calls `IRequestHeaderEnricher.Apply()` |
| `Enrichment/RequestHeaderEnricher.cs` | Sets `X-User-Id`, `X-User-Roles`, `X-Merchant-Id`, `X-Permission-Scope`, `X-Forwarded-For/Host`; strips `Cookie` + `Authorization` |
| `Error/GatewayErrorResponseWriter.cs` | Writes JSON `{request_id, timestamp, error, message}` responses |
| `Observability/GatewayStructuredRequestLoggingMiddleware.cs` | Structured `LogInformation`/`LogWarning` per request (skips health probes) |
| `Rbac/EndpointAccessRegistry.cs` | `IEndpointAccessRegistry` impl — longest-prefix matching over `GatewayRbacOptions` |
| `Rbac/GatewayRbacMiddleware.cs` | Enforces Public / Authenticated / Permission per route; sets `PermissionScope` in `HttpContext.Items` |
| `Rbac/GatewayRbacOptionsSetup.cs` | Default public routes (health, `/connect/*`, catalog GET, etc.) |
| `Rbac/PermissionClaimReader.cs` | Reads `permission` claims + wildcard admin check from JWT |
| `ReverseProxy/YarpGatewayReverseProxy.cs` | `IGatewayReverseProxy` impl — `AddReverseProxy().LoadFromConfig()` |

---

## Middleware Pipeline Order

Defined in `GatewayRequestPipelineExtensions.UseGatewayDownstreamPipeline()`:

```
UseCors(EdgeCorsPolicyNames.Default)           ← UseGatewayEdgeCors()
  → GatewayRequestCorrelationMiddleware        ← assign X-Request-Id
  → UseRateLimiter                             ← sliding-window buckets
  → UseAuthentication                          ← JWT Bearer
  → UseAuthorization
  → GatewayRbacMiddleware                      ← Public/Authenticated/Permission check
  → GatewayRequestEnrichmentMiddleware         ← inject downstream headers
  → GatewayStructuredRequestLoggingMiddleware  ← log timing + status
  → MapGatewayReverseProxy()                   ← YARP forwarding
```

---

## Configuration Sections

| Section | Bound to | Required |
|---|---|---|
| `Cors` | `CorsEdgeOptions` | Yes — at least one `AllowedOrigins` |
| `Jwt` | `GatewayJwtOptions` | Yes — `Authority` (or env `services__identity__*`) |
| `GatewayRbac` | `GatewayRbacOptions` | No — defaults filled by `GatewayRbacOptionsSetup` |
| `RateLimit` | `RateLimitingOptions` | No — defaults: 1000 req/60 s global |
| `Kestrel:Edge` | `KestrelEdgeOptions` | No — only needed for in-process TLS |
| `ReverseProxy` | YARP | Yes — `ReverseProxy:Routes` must have children |

Authority resolution order (first non-null wins):
`services__identity__https__0` → `services__identity__http__0` → `IdentityServer:Authority` → `Jwt:Authority`

---

## Rate Limit Partitions

| Partition | Key | Default |
|---|---|---|
| Health (`/health`, `/alive`) | `health:{ip}` | 100 000 req/60 s |
| Auth (`/api/account`, `/connect`) | `auth:{ip}` | 10 req/60 s |
| Search (path contains `/search`) | `search:{ip}` | 60 req/60 s |
| Write (POST/PUT/PATCH/DELETE) | `write:{sub or ip}` | 50 req/60 s |
| Authenticated user | `user:{sub}` | 1000 req/60 s |
| Global (anonymous) | `global:{ip}` | 1000 req/60 s |

---

## RBAC — Adding a Protected Route

In `appsettings.json` under `GatewayRbac:Rules`:

```json
"GatewayRbac": {
  "Rules": [
    {
      "PathPrefix": "/api/v1/products",
      "Methods": "POST,PUT,PATCH,DELETE",
      "OwnPermission": "catalog:write:own",
      "AllPermission": "catalog:write:all"
    }
  ]
}
```

Fields: `PathPrefix`, `Methods` (comma-separated or `*`), `OwnPermission`, `AllPermission`, `RequireAuthenticatedOnly`, `RequiresMfa`.

RBAC result sets `HttpContext.Items[GatewayContextItems.PermissionScope]` to `"own"` or `"all"`, then enrichment forwards it as `X-Permission-Scope` header to downstream services.

---

## Key Constants — Quick Reference

```csharp
// Namespaces
using UrbanX.Gateway.Application.Constants;       // GatewayHeaderNames, GatewayErrorCodes, GatewayContextItems
using UrbanX.Gateway.Application.Configuration;   // CorsEdgeOptions, KestrelEdgeOptions, EdgeCorsPolicyNames

// Headers
GatewayHeaderNames.XRequestId        // "X-Request-Id"
GatewayHeaderNames.XUserId           // "X-User-Id"
GatewayHeaderNames.XUserRoles        // "X-User-Roles"
GatewayHeaderNames.XPermissionScope  // "X-Permission-Scope"
GatewayHeaderNames.XMerchantId       // "X-Merchant-Id"

// Error codes (JSON "error" field)
GatewayErrorCodes.Unauthorized       // "UNAUTHORIZED"
GatewayErrorCodes.Forbidden          // "FORBIDDEN"
GatewayErrorCodes.RateLimitExceeded  // "RATE_LIMIT_EXCEEDED"
GatewayErrorCodes.MfaRequired        // "MFA_REQUIRED"

// HttpContext.Items
GatewayContextItems.PermissionScope  // "PermissionScope"
```

---

## How to Add a New Middleware

1. Create class in `Infrastructure/<concern>/<Name>Middleware.cs`
2. Constructor takes `RequestDelegate _next` (+ any DI)
3. Add `UseMiddleware<YourMiddleware>()` in `GatewayRequestPipelineExtensions.UseGatewayDownstreamPipeline()` at the correct position
4. If it needs DI registration, add it in `GatewayInfrastructureServiceCollectionExtensions.AddGatewayInfrastructure()`

## How to Write a JSON Error Response

```csharp
await GatewayErrorResponseWriter.WriteAsync(
    context,
    StatusCodes.Status403Forbidden,
    GatewayErrorCodes.Forbidden,
    "You are not allowed to call this resource.");
```
