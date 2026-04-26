# Google OAuth Login

External login via Google is provisioned through Duende IdentityServer's external authentication pipeline.

## Setup

1. Create OAuth Client ID in Google Cloud Console
2. Authorized redirect URI: `http://localhost:5005/signin-google` (Identity service callback)
3. Set `Google:ClientId` and `Google:ClientSecret` in `appsettings.Development.json` or via user-secrets:

```bash
cd src/Services/Identity/UrbanX.Identity.API
dotnet user-secrets init
dotnet user-secrets set "Google:ClientId" "<id>.apps.googleusercontent.com"
dotnet user-secrets set "Google:ClientSecret" "<secret>"
```

If either value is empty, the Google handler is **not registered** (Program.cs guards against that).

## Flow

1. SPA → `GET /api/account/external/google?returnUrl=/` (redirects to Google)
2. Google → user consents → `GET /signin-google?code=...` (Identity callback)
3. Identity creates or links `ApplicationUser` (matching by email), sets `EmailConfirmed = true`
4. Audit log: `EXTERNAL_LOGIN_GOOGLE`
5. Returns OIDC token (or session cookie) to SPA via `returnUrl`

> Note: the explicit `/api/account/external/*` endpoint is reserved but not yet implemented in v1 — Duende's built-in `Account/Login?returnUrl=...` handles external buttons. Customize via `IdentityServer.Pages` if a tailored UI is needed.

## Files

- Program.cs: Google scheme registration via `AddGoogle(IdentityServerConstants.ExternalCookieAuthenticationScheme, ...)`
- Whitelisted in [GatewayRbacOptionsSetup.cs](../../src/Gateway/UrbanX.Gateway.Infrastructure/Rbac/GatewayRbacOptionsSetup.cs) — `/signin-google` and `/api/account/external` are public
