namespace UrbanX.Identity.Domain.ValueObjects
{
    public static class AuthEventType
    {
        public const string Registered = "REGISTERED";
        public const string EmailConfirmed = "EMAIL_CONFIRMED";
        public const string LoginSuccess = "LOGIN_SUCCESS";
        public const string LoginFailed = "LOGIN_FAILED";
        public const string Logout = "LOGOUT";
        public const string PasswordChanged = "PASSWORD_CHANGED";
        public const string PasswordResetRequested = "PASSWORD_RESET_REQUESTED";
        public const string PasswordReset = "PASSWORD_RESET";
        public const string ProfileUpdated = "PROFILE_UPDATED";
        public const string RoleAssigned = "ROLE_ASSIGNED";
        public const string RoleRevoked = "ROLE_REVOKED";
        public const string AccountLocked = "ACCOUNT_LOCKED";
        public const string AccountDeactivated = "ACCOUNT_DEACTIVATED";
        public const string AccountActivated = "ACCOUNT_ACTIVATED";
        public const string ExternalLoginGoogle = "EXTERNAL_LOGIN_GOOGLE";
    }
}
