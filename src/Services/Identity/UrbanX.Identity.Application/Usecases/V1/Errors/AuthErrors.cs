using Shared.Kernel.Primitives;

namespace UrbanX.Identity.Application.Usecases.V1.Errors;

public static class AuthErrors
{
    public static readonly Error InvalidCredentials =
        new("Auth.InvalidCredentials", "Invalid email or password");

    public static readonly Error EmailNotConfirmed =
        new("Auth.EmailNotConfirmed", "Email has not been confirmed");

    public static readonly Error AccountLocked =
        new("Auth.AccountLocked", "Account is locked. Try again later");

    public static readonly Error AccountDeactivated =
        new("Auth.AccountDeactivated", "Account has been deactivated");

    public static Error EmailAlreadyExists(string email) =>
        new("Auth.EmailAlreadyExists", $"Email {email} is already registered");

    public static readonly Error InvalidConfirmationToken =
        new("Auth.InvalidConfirmationToken", "Email confirmation token is invalid or expired");

    public static readonly Error InvalidPasswordResetToken =
        new("Auth.InvalidPasswordResetToken", "Password reset token is invalid or expired");

    public static Error WeakPassword(string detail) =>
        new("Auth.WeakPassword", detail);

    public static Error UserNotFound(Guid id) =>
        new("User.NotFound", $"User {id} not found");

    public static Error UserNotFoundByEmail(string email) =>
        new("User.NotFoundByEmail", $"No user with email {email}");

    public static Error RoleNotFound(string role) =>
        new("Role.NotFound", $"Role {role} not found");

    public static Error UserAlreadyInRole(string role) =>
        new("User.AlreadyInRole", $"User is already in role {role}");

    public static Error UserNotInRole(string role) =>
        new("User.NotInRole", $"User is not in role {role}");

    public static readonly Error CannotChangeOwnRole =
        new("User.CannotChangeOwnRole", "You cannot change your own role assignments");

    public static readonly Error CurrentPasswordIncorrect =
        new("Auth.CurrentPasswordIncorrect", "Current password is incorrect");
}
