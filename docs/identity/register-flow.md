# Register + Email Confirmation Flow

## Endpoint

`POST /api/account/register` (public)

## Request

```json
{
  "email": "alice@example.com",
  "password": "P@ssw0rd!",
  "displayName": "Alice",
  "phoneNumber": "+84901234567"
}
```

Password rules: ≥ 8 chars, must contain upper + lower + digit.

## Response — 201 Created

```json
{
  "userId": "11111111-...",
  "email": "alice@example.com",
  "requiresEmailConfirmation": true
}
```

## Behavior

1. Normalize email to lowercase
2. Reject if email already exists → 409 `Auth.EmailAlreadyExists`
3. Create `ApplicationUser` with `EmailConfirmed = false`
4. Assign default role `customer` (with permission claims)
5. Generate email confirmation token (`UserManager.GenerateEmailConfirmationTokenAsync`)
6. Send confirmation email via `IEmailSender.SendEmailConfirmationAsync` (logged in dev)
7. Audit log: `REGISTERED`
8. Outbox publish: `UserRegisteredV1` integration event

## Confirm Email

`POST /api/account/confirm-email`

```json
{ "userId": "11111111-...", "token": "<from email>" }
```

Returns 204 NoContent on success, or 400 `Auth.InvalidConfirmationToken` if invalid/expired.

Audit log: `EMAIL_CONFIRMED`

## Files

- Command: [RegisterUserCommand.cs](../../src/Services/Identity/UrbanX.Identity.Application/Usecases/V1/Command/RegisterUser/RegisterUserCommand.cs)
- Handler: [RegisterUserCommandHandler.cs](../../src/Services/Identity/UrbanX.Identity.Application/Usecases/V1/Command/RegisterUser/RegisterUserCommandHandler.cs)
- Confirm: [ConfirmEmailCommand.cs](../../src/Services/Identity/UrbanX.Identity.Application/Usecases/V1/Command/ConfirmEmail/ConfirmEmailCommand.cs)
- Endpoint: [AccountApis.cs](../../src/Services/Identity/UrbanX.Identity.API/Apis/AccountApis.cs)
