namespace UrbanX.Identity.API.Quickstart;

internal static class AccountOptions
{
    public const bool AllowLocalLogin = true;
    public const bool AllowRememberLogin = true;
    public static readonly TimeSpan RememberMeLoginDuration = TimeSpan.FromDays(30);

    public const bool ShowLogoutPrompt = true;
    public const bool AutomaticRedirectAfterSignOut = true;

    public const string InvalidCredentialsErrorMessage = "Email hoặc mật khẩu không đúng.";
    public const string AccountLockedErrorMessage = "Tài khoản đã bị khóa tạm thời. Vui lòng thử lại sau 15 phút.";
    public const string AccountInactiveErrorMessage = "Tài khoản chưa được kích hoạt hoặc đã bị vô hiệu hóa.";
}
