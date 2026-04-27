using System.ComponentModel.DataAnnotations;

namespace UrbanX.Identity.API.Quickstart.ViewModels;

public class LoginInputModel
{
    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberLogin { get; set; }

    public string? ReturnUrl { get; set; }
}
