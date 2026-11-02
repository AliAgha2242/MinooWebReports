using Microsoft.AspNetCore.Mvc;
using MinooWebReports.Models;
using MinooWebReports.Services;

namespace MinooWebReports.Controllers;

public sealed class HomeController(
    ISelectedConnectionService selectedConnection,
    IServerDirectoryService serverDirectory,
    ILogger<HomeController> logger) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        PopulateViewData();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Select(decimal vahedetejariSn)
    {
        var connection = serverDirectory.GetBySn(vahedetejariSn);
        if (connection is null)
        {
            ModelState.AddModelError(string.Empty, "واحد تجاری انتخاب‌شده در AppConfig.json معتبر نیست.");
            PopulateViewData();
            return View("Index");
        }

        await selectedConnection.SelectAsync(connection);
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

    [HttpGet]
    public IActionResult Error() => View();

    private void PopulateViewData()
    {
        try
        {
            ViewBag.Connections = serverDirectory.GetAll();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Minoo server configuration could not be loaded.");
            ViewBag.Connections = Array.Empty<VahedTejari>();
            ViewBag.ConfigurationError = "دریافت فهرست سرورها از AppConfig.json انجام نشد. تنظیمات سرورها را بررسی کنید.";
        }

        ViewBag.SelectedConnection = selectedConnection.Current;
    }
}
