namespace MinooWebReports.Models;

public sealed class UserModel
{
    // Preserve the original string UserCode, including leading zeroes.
    public string UserCode { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
}
