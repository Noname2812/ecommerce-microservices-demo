# Quickstart UI

Razor Pages MVC UI cho Duende IdentityServer — render login form, logout prompt, consent screen, error pages mà OIDC flow yêu cầu.

## Cấu trúc

```
src/Services/Identity/UrbanX.Identity.API/
├── Quickstart/
│   ├── AccountController.cs        # Login, Logout, AccessDenied (route /Account/*)
│   ├── ExternalController.cs       # Google OAuth challenge + callback (route /External/*)
│   ├── ConsentController.cs        # Consent prompt khi client RequireConsent=true
│   ├── HomeController.cs           # Trang chủ + /Home/Error
│   ├── AccountOptions.cs           # Toggle local login, remember me, prompts
│   ├── ConsentOptions.cs           # Toggle offline_access scope, error messages
│   ├── Extensions.cs               # LoadingPage helper cho native client redirect
│   └── ViewModels/                 # LoginInputModel, LoginViewModel, LogoutViewModel, LoggedOutViewModel, ConsentViewModel, ScopeViewModel, ErrorViewModel, RedirectViewModel
├── Views/
│   ├── _ViewImports.cshtml
│   ├── _ViewStart.cshtml
│   ├── Account/{Login,Logout,LoggedOut,AccessDenied}.cshtml
│   ├── Consent/Index.cshtml
│   ├── Home/{Index,Error}.cshtml
│   └── Shared/{_Layout,_ValidationSummary,_ValidationScriptsPartial,_ScopeItem,Redirect}.cshtml
└── wwwroot/css/site.css
```

## Endpoints

| Method | Path | Mục đích |
|---|---|---|
| GET | `/Account/Login?returnUrl=...` | Render form đăng nhập |
| POST | `/Account/Login` | Xử lý đăng nhập (`PasswordSignInAsync` + audit log) |
| GET | `/Account/Logout?logoutId=...` | Hiển thị logout prompt |
| POST | `/Account/Logout` | Signout cookie + Duende session |
| GET | `/Account/AccessDenied` | Hiển thị "Truy cập bị từ chối" |
| GET | `/External/Challenge?scheme=Google&returnUrl=...` | Khởi tạo external auth challenge |
| GET | `/External/Callback` | Callback từ external provider, auto-provision local user nếu chưa có |
| GET | `/Consent/Index?returnUrl=...` | Hiển thị consent screen (yêu cầu authenticated) |
| POST | `/Consent/Index` | Xử lý chọn yes/no + scopes |
| GET | `/Home/Error?errorId=...` | Trang lỗi từ Duende `IIdentityServerInteractionService.GetErrorContextAsync` |

## Flow trong context OIDC PKCE

1. SPA → `GET /connect/authorize?...` (Duende)
2. Duende phát hiện chưa authenticated → redirect `/Account/Login?returnUrl=/connect/authorize/callback?...`
3. User submit form → `POST /Account/Login` → `SignInManager.PasswordSignInAsync` → cookie `urbanx.identity` set
4. Redirect lại `returnUrl` → Duende xử lý tiếp `/connect/authorize` → check consent
5. Nếu client `RequireConsent=true` (urbanx-spa) → Duende redirect `/Consent/Index`
6. User chọn scopes → `POST /Consent/Index` → Duende generate auth code → 302 về `redirect_uri` của SPA
7. SPA exchange code tại `POST /connect/token`

## Wiring (Program.cs)

```csharp
// services
builder.Services.AddControllersWithViews();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "urbanx.identity";
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

// pipeline
app.UseStaticFiles();
app.UseRouting();
app.UseIdentityServer();
app.UseAuthorization();

app.MapCarter();
app.MapDefaultControllerRoute();
```

Order: `UseStaticFiles` trước `UseRouting`. `UseIdentityServer` trước `UseAuthorization`.

## Audit Log

`AccountController.Login` ghi audit qua `IIdentityAuditWriter`:

| Sự kiện | EventType |
|---|---|
| Login thành công | `LOGIN_SUCCESS` |
| Sai password | `LOGIN_FAILED` (metadata: `reason=invalid_credentials`) |
| User inactive | `LOGIN_FAILED` (metadata: `reason=inactive`) |
| Bị lock | `ACCOUNT_LOCKED` |
| Logout | `LOGOUT` |
| Google login | `EXTERNAL_LOGIN_GOOGLE` (metadata: `provider=Google`) |

Tự động capture IP + UA qua `IHttpContextAccessor` trong `IdentityAuditWriter`.

## Account Lockout

`SignInManager.PasswordSignInAsync(..., lockoutOnFailure: true)` → đếm attempts. Đạt `Identity:Lockout:MaxFailedAccessAttempts` (default 5) → user lock `Identity:Lockout:DefaultLockoutMinutes` (default 15). UI hiển thị `AccountOptions.AccountLockedErrorMessage`.

## Consent

Client `urbanx-spa` đã set `RequireConsent = true` + `AllowRememberConsent = true`. Lần đầu user authorize, Duende redirect `/Consent/Index`. User tick "Ghi nhớ" → Duende lưu grant trong `PersistedGrants` table → các authorize request tiếp theo bypass consent.

Nếu muốn tắt consent (first-party trust), set `RequireConsent = false` trong `IdentityServerResources.cs`.

## Google OAuth

Đã wire trong Program.cs (chỉ register khi `Google:ClientId` + `Google:ClientSecret` được set):

```bash
cd src/Services/Identity/UrbanX.Identity.API
dotnet user-secrets set "Google:ClientId" "<...>.apps.googleusercontent.com"
dotnet user-secrets set "Google:ClientSecret" "<secret>"
```

Callback path `/signin-google` (đã whitelist ở Gateway). Login form sẽ tự hiện nút "Đăng nhập với Google" khi scheme này được register (dùng `IAuthenticationSchemeProvider.GetAllSchemesAsync`).

`ExternalController.AutoProvisionUserAsync`: tạo `ApplicationUser` mới nếu chưa có (email + name từ Google claims), `EmailConfirmed = true`, `IsActive = true`. Add vào table `user_logins` qua `UserManager.AddLoginAsync`.

## Branding / Customization

- Layout: [Views/Shared/_Layout.cshtml](../../src/Services/Identity/UrbanX.Identity.API/Views/Shared/_Layout.cshtml) — Bootstrap 5.3 CDN
- CSS custom: [wwwroot/css/site.css](../../src/Services/Identity/UrbanX.Identity.API/wwwroot/css/site.css)
- Error messages tiếng Việt: `AccountOptions.cs`, `ConsentOptions.cs`

Đổi sang theme khác bằng cách sửa CDN link trong `_Layout.cshtml` hoặc tải Bootstrap về `wwwroot/lib/`.

## Configuration

Không thêm config mới. Dùng các keys hiện có:

```json
{
  "Identity": { "Lockout": { "MaxFailedAccessAttempts": 5, "DefaultLockoutMinutes": 15 } },
  "Google": { "ClientId": "", "ClientSecret": "" }
}
```

## Test Manual

1. Aspire start → Identity ở `http://localhost:5005`
2. Mở `http://localhost:5005/` → trang Index (chỉ hiển thị ở Development)
3. Test PKCE flow bằng cURL hoặc browser:
   ```
   http://localhost:5005/connect/authorize?
     client_id=urbanx-spa&
     response_type=code&
     redirect_uri=http://localhost:5173/auth/callback&
     scope=openid profile email urbanx-api offline_access&
     code_challenge=<base64-S256>&
     code_challenge_method=S256&
     state=xyz
   ```
4. Đăng nhập bằng `admin@urbanx.local` / `Admin@123456`
5. Consent screen hiển thị → tick scopes → Đồng ý
6. Browser redirect về `http://localhost:5173/auth/callback?code=...&state=xyz` (sẽ 404 vì SPA chưa build — đó là behavior đúng cho phase này)

## Limitations / Out of Scope

- **Forgot password / Reset password UI**: hiện chỉ có API endpoint (`/api/account/forgot-password`, `/api/account/reset-password`). Razor UI sẽ thêm sau khi SPA UX cần dùng.
- **Register UI**: cũng chỉ có API. SPA sẽ tự render register form và POST `/api/account/register`.
- **2FA**: chưa hỗ trợ. ASP.NET Identity sẵn sàng (`UserManager.GetTwoFactorEnabledAsync`), thêm khi v2.
- **Remember consent revoke UI**: user chưa có cách tự revoke grant. Admin phải xóa qua DB hoặc API riêng.
