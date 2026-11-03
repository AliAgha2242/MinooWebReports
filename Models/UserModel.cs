namespace MinooWebReports.Models;

public sealed class UserModel
{
    public decimal UsersKeyId { get; set; }
    public int UserId { get; set; }
    // UserCode is the original string login identifier; do not parse it as a number.
    public string UserCode { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public decimal? VahedeTejariSn { get; set; }
    public string ExpireDate { get; set; } = string.Empty;
}