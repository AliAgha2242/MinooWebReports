using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MinooWebReports.Models;

namespace MinooWebReports.Controllers;

[AllowAnonymous]
[Route("sso")]
public sealed class SsoController(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<SsoController> logger) : Controller
{
    [HttpPost("consume")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> Consume([FromForm] string? ticket, CancellationToken cancellationToken)
    {
        Response.Headers["Cache-Control"] = "no-store, no-cache";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";

        if (string.IsNullOrWhiteSpace(ticket) || ticket.Length > 256)
            return Unauthorized();

        var redeemUrl = configuration["Sso:Parent:RedeemUrl"];
        var clientSecret = configuration["Sso:Parent:ClientSecret"];
        if (!Uri.TryCreate(redeemUrl, UriKind.Absolute, out var redeemUri) ||
            (redeemUri.Scheme != Uri.UriSchemeHttp && redeemUri.Scheme != Uri.UriSchemeHttps) || string.IsNullOrWhiteSpace(clientSecret))
        {
            logger.LogWarning("Parent SSO is not configured; showing the local Reports login.");
            return RedirectToAction("Login", "Account");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, redeemUri)
            {
                Content = JsonContent.Create(new SsoTicketRedeemRequest(ticket))
            };
            request.Headers.TryAddWithoutValidation("X-Reports-Client-Secret", clientSecret);
            using var response = await httpClientFactory.CreateClient("MinooWebCore")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return Unauthorized();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Parent SSO returned {StatusCode}; showing the local Reports login.",
                    (int)response.StatusCode);
                return RedirectToAction("Login", "Account");
            }

            var user = await response.Content.ReadFromJsonAsync<SsoUserResponse>(cancellationToken: cancellationToken);
            if (user is null || string.IsNullOrWhiteSpace(user.UserId) ||
                string.IsNullOrWhiteSpace(user.UserCode) || string.IsNullOrWhiteSpace(user.UserName))
                return Unauthorized();

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim("UserCode", user.UserCode),
                new Claim(ClaimTypes.Role, "User")
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    AllowRefresh = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
                });

            return RedirectToAction("Index", "Home");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Parent SSO is unavailable; showing the local Reports login.");
            return RedirectToAction("Login", "Account");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Parent SSO redemption timed out; showing the local Reports login.");
            return RedirectToAction("Login", "Account");
        }
    }
}
