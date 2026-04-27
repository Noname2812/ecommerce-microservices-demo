# Google OAuth Login

External login Google được wire qua Duende's external authentication pipeline + Quickstart UI's `ExternalController`.

## Setup

1. Tạo OAuth Client ID ở Google Cloud Console
2. Authorized redirect URI: `http://localhost:5005/signin-google` (Identity service callback — KHÔNG phải Gateway)
3. Set `Google:ClientId` và `Google:ClientSecret` qua user-secrets:

```bash
cd src/Services/Identity/UrbanX.Identity.API
dotnet user-secrets init
dotnet user-secrets set "Google:ClientId" "<id>.apps.googleusercontent.com"
dotnet user-secrets set "Google:ClientSecret" "<secret>"
```

Nếu một trong hai giá trị rỗng, Google handler **không được register** ([Program.cs](../../src/Services/Identity/UrbanX.Identity.API/Program.cs) guard).

## Flow

End-to-end qua Gateway BFF:

1. User đang ở Gateway `/Account/Login` page (Quickstart UI render qua redirect chain `/bff/login` → Identity `/connect/authorize` → Identity `/Account/Login?returnUrl=...`)
2. Login form hiển thị nút "Đăng nhập với Google" (auto-detect khi scheme `Google` được register, qua `IAuthenticationSchemeProvider.GetAllSchemesAsync` trong [`AccountController.BuildLoginViewModelAsync`](../../src/Services/Identity/UrbanX.Identity.API/Quickstart/AccountController.cs))
3. User click nút → `GET /External/Challenge?scheme=Google&returnUrl=...` ([`ExternalController.Challenge`](../../src/Services/Identity/UrbanX.Identity.API/Quickstart/ExternalController.cs))
4. ASP.NET Core `Challenge(scheme: "Google")` → 302 đến `accounts.google.com/o/oauth2/v2/auth?...&redirect_uri=http://localhost:5005/signin-google`
5. User consent ở Google → 302 → Identity `/signin-google?code=...`
6. Google handler trao đổi code, set external cookie `idsrv.external` → 302 đến `/External/Callback`
7. [`ExternalController.Callback`](../../src/Services/Identity/UrbanX.Identity.API/Quickstart/ExternalController.cs):
   - Đọc external principal
   - `UserManager.FindByLoginAsync(provider, providerUserId)` — nếu chưa tồn tại → `AutoProvisionUserAsync` tạo `ApplicationUser` mới (email + name từ Google claims, `EmailConfirmed = true`, `IsActive = true`) + `UserManager.AddLoginAsync` ghi `user_logins`
   - Check `IsActive` → reject nếu user bị deactivate
   - `SignInManager.SignInWithClaimsAsync` set local cookie `urbanx.identity`
   - SignOut external cookie
   - Audit log: `EXTERNAL_LOGIN_GOOGLE` với metadata `provider=Google`
   - 302 về `returnUrl` của OIDC flow → Duende tiếp tục `/connect/authorize` → 302 redirect_uri của client (Gateway `/signin-oidc` cho BFF)

## Files

- [Program.cs](../../src/Services/Identity/UrbanX.Identity.API/Program.cs): Google scheme register qua `AddGoogle(IdentityServerConstants.ExternalCookieAuthenticationScheme, ...)`
- [ExternalController.cs](../../src/Services/Identity/UrbanX.Identity.API/Quickstart/ExternalController.cs): Challenge + Callback + AutoProvisionUserAsync
- [Login.cshtml](../../src/Services/Identity/UrbanX.Identity.API/Views/Account/Login.cshtml): nút "Đăng nhập với Google"
- Gateway whitelist `/signin-google` ở [GatewayRbacOptionsSetup.cs](../../src/Gateway/UrbanX.Gateway.Infrastructure/Rbac/GatewayRbacOptionsSetup.cs) — public route, đi qua YARP `identity-signin-google-route` (cùng domain với Identity vì callback URI ở Google Console phải khớp Identity URL)

## Limitations

- Chỉ wire 1 external provider (Google). Thêm Facebook/GitHub: thêm `AddFacebook`/`AddGitHub` trong [Program.cs](../../src/Services/Identity/UrbanX.Identity.API/Program.cs), set `DisplayName` để Quickstart UI tự render thêm nút
- Email-merge: nếu user đã đăng ký bằng email/password trước, login Google với cùng email sẽ tạo user mới (vì match qua `UserLogin` chứ không phải email). Cần thêm logic merge ở `Callback` nếu muốn auto-link
