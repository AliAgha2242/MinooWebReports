namespace MinooWebReports.Models;

public sealed record SsoTicketRedeemRequest(string Ticket);

public sealed class SsoUserResponse
{
    public string UserId { get; set; } = string.Empty;
    public string UserCode { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
}
