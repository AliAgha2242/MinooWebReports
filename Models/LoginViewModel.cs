using System.ComponentModel.DataAnnotations;

namespace MinooWebReports.Models;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "نام کاربری را وارد کنید.")]
    [Display(Name = "نام کاربری")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "رمز عبور را وارد کنید.")]
    [DataType(DataType.Password)]
    [Display(Name = "رمز عبور")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "مرا به خاطر بسپار")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}
