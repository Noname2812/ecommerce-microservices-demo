# Gateway BFF (Backend-for-Frontend)

Gateway đóng vai trò OIDC client thay cho SPA. Nhận login challenge → redirect tới Identity → exchange code → lưu access/refresh token vào HttpOnly cookie session ở phía Gateway. SPA chỉ cần fetch `/bff/user` + redirect tới `/bff/login`.

## Stack

- **Duende.BFF** v3.0.0 — cookie session, login/logout/user endpoints, server-side session storage
- **Duende.BFF.Yarp** v3.0.0 — YARP integration: tự attach `Authorization: Bearer <access_token>` từ session vào outgoing request
- Cookie scheme + OIDC scheme (Microsoft.AspNetCore.Authentication.{Cookies,OpenIdConnect})

## Pipeline

```
UseCors → correlation → rate limit
  → UseAuthentication       (cookie scheme là default)
  → UseBff                  (BFF middleware: anti-forgery, session check)
  → UseAuthorization
  → GatewayRbacMiddleware
  → RequestEnrichmentMiddleware    (set X-User-* headers; strip Cookie + Authorization)
  → StructuredRequestLoggingMiddleware

MapBffManagementEndpoints  (creates /bff/login, /bff/logout, /bff/user, /bff/silent-login)
MapReverseProxy(UseAntiforgeryCheck)  (BFF transforms attach access_token vào outgoing request)
```

## BFF Endpoints (auto-mapped)

| Method | Path | Mục đích |
|---|---|---|
| GET | `/bff/login?returnUrl=/foo` | Trigger OIDC challenge → 302 tới Identity `/connect/authorize` |
| GET | `/signin-oidc` | OIDC callback từ Identity. Exchange code → set cookie `urbanx.bff` → redirect về `returnUrl` |
| POST | `/bff/logout?sid=...` | Clear local cookie + 302 tới Identity `/connect/endsession` |
| GET | `/signout-callback-oidc` | Callback sau khi Identity logout |
| GET | `/bff/user` | Trả về JSON claims của user hiện tại (yêu cầu cookie). 401 nếu chưa auth |
| GET | `/bff/silent-login` | Silent renew (refresh access_token in background) |

Tất cả endpoints này yêu cầu header `X-CSRF: 1` trừ `/bff/login` và callback paths (anti-forgery middleware do BFF cung cấp).

## Configuration

`appsettings.json` Gateway:

```json
"Bff": {
  "ClientId": "urbanx-bff",
  "ClientSecret": "dev-bff-secret",
  "CookieName": "urbanx.bff",
  "FrontendUrl": "http://localhost:5173",
  "SessionLifetimeMinutes": 60,
  "Scopes": [ "openid", "profile", "email", "offline_access", "roles", "merchant", "urbanx-api" ]
}
```

Authority resolution: [`IdentityAuthorityResolver.Resolve`](../../src/Gateway/UrbanX.Gateway.Infrastructure/Bff/IdentityAuthorityResolver.cs) đọc lần lượt: Aspire env `services__identity__https__0` → `services__identity__http__0` → `IdentityServer:Authority`.

Identity-side: `urbanx-bff` client trong [IdentityServerResources.cs](../../src/Services/Identity/UrbanX.Identity.API/Configuration/IdentityServerResources.cs) — `RequireClientSecret = true`, `RedirectUris = {bff_base_url}/signin-oidc`, `AllowedGrantTypes = Code` + PKCE.

Identity `appsettings.json` cần config Bff URL để build redirect:

```json
"Bff": {
  "BaseUrl": "http://localhost:5050",
  "ClientSecret": "dev-bff-secret"
}
```

## Pinned Ports (dev)

| Service | URL | Pinned trong |
|---|---|---|
| Gateway | `http://localhost:5050` | AppHost `WithHttpEndpoint(port: 5050)` |
| Identity | `http://localhost:5005` | AppHost `WithHttpEndpoint(port: 5005)` |
| SPA (Vite) | `http://localhost:5173` | (Phase 3 — chưa scaffold) |

Dev SPA dùng Vite proxy `/bff/*`, `/api/*`, `/signin-oidc`, `/signout-callback-oidc` → Gateway. Browser thấy mọi thứ same-origin trên `:5173` → cookie hoạt động.

## Token Storage Flow

```
1. Browser → Gateway /bff/login → 302 → Identity /connect/authorize
2. User login UI ở Identity → 302 back → Gateway /signin-oidc?code=...
3. Gateway server-to-server exchange POST /connect/token (using client secret)
   → access_token + refresh_token + id_token
4. Gateway store tokens vào server-side session, set cookie urbanx.bff (HttpOnly)
5. Browser tiếp theo gọi Gateway /api/v1/products → cookie urbanx.bff đính kèm
6. Cookie auth scheme đọc cookie → User principal populated từ ID token claims
7. RbacMiddleware check permission OK → ProxyTransform attach access_token vào outgoing request
8. RequestHeaderEnricher strip Cookie + Authorization, set X-User-Id / X-Permission-Scope
9. Backend service nhận X-User-* (Trust-the-Gateway)
```

Lưu ý: Step 7 và 8 — BFF.Yarp transforms attach Authorization, sau đó enrichment STRIPS nó (vì backend dùng X-User-*, không validate JWT). Hai cơ chế song song.

## Anti-forgery

Browser fetch tới `/bff/*` và `/api/*` qua cookie phải có header `X-CSRF: 1` (anti-CSRF token). Frontend axios:

```ts
axios.defaults.withCredentials = true;
axios.defaults.headers.common['X-CSRF'] = '1';
```

Top-level navigation (`<a href="/bff/login">` hoặc `window.location`) KHÔNG cần header — anti-forgery chỉ áp dụng cho fetch/XHR.

## Server-Side Sessions

Hiện dùng `AddServerSideSessions()` mặc định = in-memory store. Khi scale-out cần switch sang Redis/SQL:

```csharp
services.AddBff()
    .AddServerSideSessions()
    .AddSessionStore<RedisSessionStore>();  // example
```

In-memory store → khi Gateway restart, mọi user session bị invalidate (browser nhận 401 → cookie bị reject).

## Logout

```
Browser → POST /bff/logout?sid=<session-id>
  → Clear cookie urbanx.bff
  → Server-side session deleted
  → 302 tới Identity /connect/endsession?id_token_hint=...&post_logout_redirect_uri=...
  → Identity clear urbanx.identity cookie
  → 302 tới Gateway /signout-callback-oidc
  → 302 tới SPA root
```

Logout-everywhere (revoke refresh tokens trên tất cả devices của user) chưa support v1. Cần wire tới Identity revocation endpoint.

## Limitations / Out of Scope

- **JwtBearer scheme bỏ**: Gateway không còn validate JWT Bearer trực tiếp. Direct API testing với curl/Postman bằng JWT KHÔNG hoạt động qua Gateway nữa. Test bằng cookie flow hoặc gọi service trực tiếp (bypass Gateway, dùng X-User-* mock headers).
- **Server-side session in-memory**: không scale-out được. Production cần Redis/SQL store.
- **Mobile client**: chưa có flow riêng. Mobile sẽ cần dùng OIDC native flow (custom URI scheme) trực tiếp với Identity, KHÔNG qua BFF.
- **Token revocation on logout**: chỉ revoke session locally, refresh_token chưa revoke ở Identity. Cần wire `/connect/revocation` call.
- **Anti-forgery customization**: dùng default `X-CSRF: 1` header. Chuyển sang token-based CSRF nếu cần stricter.
