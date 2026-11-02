using Microsoft.AspNetCore.Mvc;
using MinooWebReports.Models;
using MinooWebReports.Services;

namespace MinooWebReports.Controllers;

public sealed class HomeController(ISelectedConnectionService selectedConnection) : Controller
{
    // Temporary data source. Replace this provider with the real unit directory later.
    private static readonly IReadOnlyList<VahedTejari> TestConnections =
    [
        new() { VahedetejariSn = 1001m, VahedetejariDs = "واحد تجاری مرکزی", Server = "sql-test-01", DataBAset = "Minoo_Test" },
        new() { VahedetejariSn = 1002m, VahedetejariDs = "واحد تجاری شعبه یک", Server = "sql-test-02", DataBAset = "Minoo_Branch1" },
        new() { VahedetejariSn = 1003m, VahedetejariDs = "واحد تجاری شعبه دو", Server = "sql-test-03", DataBAset = "Minoo_Branch2" }
    ];

    [HttpGet]
    public IActionResult Index()
    {
        ViewBag.TestConnections = TestConnections;
        ViewBag.SelectedConnection = selectedConnection.Current;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Select(decimal vahedetejariSn)
    {
        var connection = TestConnections.FirstOrDefault(x => x.VahedetejariSn == vahedetejariSn);
        if (connection is null)
        {
            ModelState.AddModelError(string.Empty, "واحد تجاری انتخاب‌شده معتبر نیست.");
            ViewBag.TestConnections = TestConnections;
            ViewBag.SelectedConnection = selectedConnection.Current;
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
}
