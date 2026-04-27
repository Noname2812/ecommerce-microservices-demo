# Login Flow

Identity does not expose a custom `/api/account/login` endpoint — login goes through Duende's standard OIDC `/connect/authorize` + `/connect/token`. Quickstart UI Razor Pages render login form (`/Account/Login`), consent (`/Consent/Index`), logout (`/Account/Logout`).

## Production flow: Gateway BFF (Auth Code + PKCE, confidential client `urbanx-bff`)

Default cho UrbanX SPA. SPA KHÔNG bao giờ chạm trực tiếp Identity — luôn qua Gateway BFF.

1. Browser → Gateway `/bff/login?returnUrl=/dashboard`
2. Gateway 302 → Identity `/connect/authorize?client_id=urbanx-bff&response_type=code&redirect_uri=http://localhost:5050/signin-oidc&scope=openid profile email urbanx-api offline_access roles merchant&code_challenge=...&code_challenge_method=S256`
3. Identity render Quickstart UI `/Account/Login` → user submit → set Identity cookie `urbanx.identity` → 302 `/Consent/Index` (lần đầu) → user đồng ý → 302 về `redirect_uri?code=...`
4. Gateway server-to-server `POST /connect/token` với `grant_type=authorization_code`, `code`, `code_verifier`, `client_id=urbanx-bff`, `client_secret`, `redirect_uri`
5. Response → access_token + refresh_token + id_token → Gateway lưu trong server-side session, set cookie `urbanx.bff` (HttpOnly) → 302 về returnUrl

Chi tiết BFF: `docs/gateway/bff.md`.

## Native SPA flow: Auth Code + PKCE (public client `urbanx-spa`)

Vẫn còn config sẵn cho trường hợp SPA chạy độc lập, không qua Gateway BFF. Hiện chưa dùng. Redirect URI: `http://localhost:5173/auth/callback`. Browser tự exchange code, lưu token trong JS storage (XSS risk). KHÔNG khuyến nghị cho production của UrbanX.

## Dev/test flow: Resource Owner Password (client `urbanx-test-password`)

```
POST /connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=password
&username=alice@example.com
&password=P@ssw0rd!
&client_id=urbanx-test-password
&client_secret=dev-secret
&scope=openid profile email urbanx-api offline_access roles merchant
```

Response:

```json
{
  "access_token": "eyJ...",
  "refresh_token": "...",
  "expires_in": 3600,
  "token_type": "Bearer",
  "scope": "openid profile email urbanx-api offline_access roles merchant"
}
```

## Refresh

Với BFF (`urbanx-bff`), Gateway tự refresh thông qua `/bff/silent-login` hoặc trên-demand khi access_token hết hạn — frontend không cần biết.

Cho dev/test trực tiếp với `urbanx-test-password`:

```
POST /connect/token
grant_type=refresh_token&refresh_token=<old>&client_id=urbanx-test-password&client_secret=dev-secret
```

Refresh tokens sliding 7 days, one-time-use để chống replay.

## Logout

- **BFF flow**: `POST /bff/logout?sid=<session-id>` (yêu cầu header `X-CSRF: 1`). Gateway clear cookie + session, 302 sang `/connect/endsession` (id_token_hint), Identity clear `urbanx.identity` cookie, redirect về `/signout-callback-oidc` → frontend.
- **Direct**: `GET /connect/endsession?id_token_hint=...&post_logout_redirect_uri=...` clear Identity session cookie.
- **Revoke specific token**: `POST /connect/revocation` với `token=<rt>&token_type_hint=refresh_token`.

## JWT Claims (issued)

| Claim | Source | Notes |
|---|---|---|
| `sub` | ApplicationUser.Id | Used as `X-User-Id` by Gateway |
| `email` | ApplicationUser.Email | |
| `role` | UserRoles → Roles | Multiple if user has many roles. Used as `X-User-Roles` |
| `merchant_id` | ApplicationUser.MerchantId | Forwarded as `X-Merchant-Id` |
| `permission` | Role claims (claim type "permission") | Used by Gateway RBAC for permission scope match |

## Account Lockout

5 consecutive failed login attempts → user locked for 15 minutes (configurable in `Identity:Lockout`).

- **Quickstart UI flow** (BFF + SPA paths): `AccountController.Login` POST nhận `result.IsLockedOut == true` từ `SignInManager.PasswordSignInAsync` → render lại form với message `AccountOptions.AccountLockedErrorMessage` ("Tài khoản đã bị khóa tạm thời...")
- **ROP grant** (`urbanx-test-password`): `/connect/token` trả 400 với `error=invalid_grant`

Audit log records `LOGIN_FAILED` (mỗi attempt) và `ACCOUNT_LOCKED` (khi `result.IsLockedOut`).
