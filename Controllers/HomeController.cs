using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MinooWebReports.Models;
using MinooWebReports.Services;

namespace MinooWebReports.Controllers;

[Authorize]
public sealed class HomeController(
    ISelectedConnectionService selectedConnection,
    ICompanyAppLocationService companyLocations,
    ILogger<HomeController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        await PopulateViewDataAsync(cancellationToken);
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Select(decimal vahedetejariSn, CancellationToken cancellationToken)
    {
        var locations = await GetAuthorizedLocationsAsync(cancellationToken);
        var connection = locations.SingleOrDefault(x => x.VahedetejariSn == vahedetejariSn);
        if (connection is null)
        {
            ModelState.AddModelError(string.Empty, "شما به واحد تجاری انتخاب‌شده دسترسی ندارید یا اطلاعات آن در تنظیمات سرورها موجود نیست.");
            await PopulateViewDataAsync(cancellationToken);
            return View("Index");
        }

        await selectedConnection.SelectAsync(connection, cancellationToken);
        TempData["Success"] = $"محل اتصال «{connection.VahedetejariDs}» انتخاب شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Clear()
    {
        await selectedConnection.ClearAsync();
        return RedirectToAction(nameof(Index));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Error() => View();

    private async Task<IReadOnlyList<VahedTejari>> GetAuthorizedLocationsAsync(CancellationToken cancellationToken)
    {
        var userCode = User.FindFirstValue("UserCode");
        if (string.IsNullOrWhiteSpace(userCode))
            throw new UnauthorizedAccessException("شناسه کاربری معتبر در نشست ورود وجود ندارد.");

        return await companyLocations.GetAllLocationAsync(new UserModel
        {
            UserCode = userCode,
            UserName = User.Identity?.Name ?? string.Empty
        }, cancellationToken);
    }

    private async Task PopulateViewDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            var locations = await GetAuthorizedLocationsAsync(cancellationToken);
            ViewBag.Connections = locations;
            var current = selectedConnection.Current;
            var authorizedCurrent = current is null
                ? null
                : locations.SingleOrDefault(x => x.VahedetejariSn == current.VahedetejariSn);
            ViewBag.SelectedConnection = authorizedCurrent;
            if (current is not null && authorizedCurrent is null)
                await selectedConnection.ClearAsync();
        }
        catch (UnauthorizedAccessException)
        {
            ViewBag.Connections = Array.Empty<VahedTejari>();
            ViewBag.ConfigurationError = "اطلاعات هویتی کاربر برای بررسی دسترسی‌ها معتبر نیست. لطفاً دوباره از درگاه مادر وارد شوید.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Authorized business-unit locations could not be loaded.");
            ViewBag.Connections = Array.Empty<VahedTejari>();
            ViewBag.ConfigurationError = "دریافت فهرست واحدهای تجاری مجاز انجام نشد. تنظیمات اتصال و دسترسی را بررسی کنید.";
        }
    }
}