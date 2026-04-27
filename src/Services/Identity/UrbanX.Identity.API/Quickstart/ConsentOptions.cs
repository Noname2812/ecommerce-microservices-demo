namespace UrbanX.Identity.API.Quickstart;

internal static class ConsentOptions
{
    public const bool EnableOfflineAccess = true;
    public const string OfflineAccessDisplayName = "Truy cập ngoại tuyến";
    public const string OfflineAccessDescription = "Cho phép ứng dụng truy cập tài nguyên thay bạn khi bạn offline.";

    public const string MustChooseOneErrorMessage = "Bạn phải chọn ít nhất một quyền.";
    public const string InvalidSelectionErrorMessage = "Lựa chọn không hợp lệ.";
}
