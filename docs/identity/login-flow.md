# Login Flow

Identity does not expose a custom `/api/account/login` endpoint — login goes through Duende's standard OIDC `POST /connect/token`.

## Recommended SPA flow: Authorization Code + PKCE

1. Browser → `GET /connect/authorize?client_id=urbanx-spa&response_type=code&redirect_uri=http://localhost:5173/auth/callback&scope=openid profile email urbanx-api offline_access&code_challenge=...&code_challenge_method=S256`
2. Identity → renders login UI (built into Duende), user logs in
3. Identity → 302 to `redirect_uri?code=...`
4. SPA → `POST /connect/token` with `grant_type=authorization_code`, `code`, `code_verifier`, `client_id=urbanx-spa`, `redirect_uri`
5. Response includes `access_token`, `refresh_token`, `id_token`. Tokens cached in HttpOnly cookies (set via Gateway BFF or directly).

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

```
POST /connect/token
grant_type=refresh_token&refresh_token=<old>&client_id=urbanx-spa
```

Refresh tokens are sliding 7 days; configured one-time-use to mitigate replay.

## Logout

```
POST /connect/endsession
```

Clears Identity session cookie. To revoke a specific refresh token: `POST /connect/revocation` with `token=<rt>&token_type_hint=refresh_token`.

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

While locked, `/connect/token` returns 400 with `error=invalid_grant`. Audit log records `LOGIN_FAILED` and `ACCOUNT_LOCKED` events.
