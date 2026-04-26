# Password Reset Flow

## Forgot Password

`POST /api/account/forgot-password`

```json
{ "email": "alice@example.com" }
```

Always returns 204 NoContent (does not leak whether email exists). If user exists and is active:
1. Generate password reset token (`UserManager.GeneratePasswordResetTokenAsync`)
2. Send email with reset link via `IEmailSender.SendPasswordResetAsync`
3. Audit log: `PASSWORD_RESET_REQUESTED`

## Reset Password

`POST /api/account/reset-password`

```json
{
  "userId": "11111111-...",
  "token": "<from email>",
  "newPassword": "NewP@ssw0rd!"
}
```

Returns 204 on success. Errors:
- 400 `Auth.InvalidPasswordResetToken` — token invalid or expired
- 400 `Auth.WeakPassword` — password fails complexity rules

Audit log: `PASSWORD_RESET`

## Change Password (authenticated)

`POST /api/v1/identity/change-password` (must include valid JWT/cookie)

```json
{ "currentPassword": "P@ssw0rd!", "newPassword": "NewP@ssw0rd!" }
```

Errors:
- 400 `Auth.CurrentPasswordIncorrect`
- 400 `Auth.WeakPassword`

Audit log: `PASSWORD_CHANGED`

Files: [ForgotPasswordCommandHandler.cs](../../src/Services/Identity/UrbanX.Identity.Application/Usecases/V1/Command/ForgotPassword/ForgotPasswordCommandHandler.cs), [ResetPasswordCommandHandler.cs](../../src/Services/Identity/UrbanX.Identity.Application/Usecases/V1/Command/ResetPassword/ResetPasswordCommandHandler.cs), [ChangePasswordCommandHandler.cs](../../src/Services/Identity/UrbanX.Identity.Application/Usecases/V1/Command/ChangePassword/ChangePasswordCommandHandler.cs).
