using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MinooWebReports.Models;
using MinooWebReports.Services;

namespace MinooWebReports.Controllers;

[Authorize]
public sealed class ReportsController(
    IReportRepository reports,
    IReportExecutionService execution,
    ISelectedConnectionService selectedConnection,
    ICompanyAppLocationService companyLocations,
    IUserReportSettingsService settings,
    ILogger<ReportsController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var authorizedConnection = await GetAuthorizedSelectedConnectionAsync(cancellationToken);
        if (selectedConnection.Current is not null && authorizedConnection is null)
            await selectedConnection.ClearAsync(cancellationToken);

        var model = new ReportsIndexViewModel
        {
            Reports = await reports.GetActiveAsync(cancellationToken),
            SelectedConnection = authorizedConnection
        };
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Run(int id, CancellationToken cancellationToken)
    {
        var report = await reports.GetByIdAsync(id, cancellationToken);
        if (report is null)
            return NotFound();

        var connection = await GetAuthorizedSelectedConnectionAsync(cancellationToken);
        if (connection is null)
        {
            TempData["Error"] = "محل اتصال انتخاب‌شده در AppConfig.json پیدا نشد. لطفاً دوباره انتخاب کنید.";
            return RedirectToAction("Index", "Home");
        }

        await selectedConnection.SelectAsync(connection, cancellationToken);
        var preferences = await settings.GetAsync(id, cancellationToken);
        string? optionsError = null;
        try
        {
            await LoadParameterOptionsAsync(report, connection, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Parameter options for report {ReportId} could not be loaded.", id);
            optionsError = "دریافت گزینه‌های پارامترهای گزارش با خطا مواجه شد. تنظیمات منبع گزینه‌ها را بررسی کنید.";
            foreach (var parameter in report.Parameters.Where(parameter =>
                         parameter.Type is "RadioButton" or "ListBox" or "ComboBox"))
                parameter.Options ??= "[]";
        }

        return View(new ReportRunViewModel
        {
            Report = report,
            SelectedConnection = connection,
            PageSize = report.NeedsPagination ? Math.Clamp(report.PageSize > 0 ? report.PageSize : 25, 10, 5000) : Math.Max(1, report.PageSize),
            Error = optionsError
        });
    }

    private async Task<VahedTejari?> GetAuthorizedSelectedConnectionAsync(CancellationToken cancellationToken)
    {
        var saved = selectedConnection.Current;
        var userCode = User.FindFirstValue("UserCode");
        if (saved is null || string.IsNullOrWhiteSpace(userCode))
            return null;

        var authorized = await companyLocations.GetAllLocationAsync(new UserModel
        {
            UserCode = userCode,
            UserName = User.Identity?.Name ?? string.Empty
        }, cancellationToken);

        return authorized.SingleOrDefault(x => x.VahedetejariSn == saved.VahedetejariSn);
    }

    private async Task LoadParameterOptionsAsync(
        ReportDefinition report,
        VahedTejari connection,
        CancellationToken cancellationToken)
    {
        foreach (var parameter in report.Parameters.Where(parameter =>
                     parameter.Type is "RadioButton" or "ListBox" or "ComboBox"))
        {
            parameter.Options = await execution.LoadParameterOptionsJsonAsync(
                parameter, connection, cancellationToken);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(
        int id,
        Dictionary<string, string[]>? submittedValues,
        Dictionary<string, string?>? filters,
        string? sortColumn,
        bool sortDescending,
        List<string>? hiddenColumns,
        List<string>? groupColumns,
        bool groupColumnsSubmitted = false,
        bool hiddenColumnsSubmitted = false,
        string? columnWidthsJson = null,
        string? gridTheme = null,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var report = await reports.GetByIdAsync(id, cancellationToken);
        if (report is null)
            return NotFound();

        var connection = await GetAuthorizedSelectedConnectionAsync(cancellationToken);
        if (connection is null)
        {
            TempData["Error"] = "محل اتصال فعال نیست یا در AppConfig.json وجود ندارد. ابتدا یک محل اتصال معتبر انتخاب کنید.";
            return RedirectToAction("Index", "Home");
        }

        await selectedConnection.SelectAsync(connection, cancellationToken);
        var values = submittedValues?.ToDictionary(
            pair => pair.Key,
            pair => string.Join(",", pair.Value ?? []),
            StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var savedPreferences = await settings.GetAsync(id, cancellationToken);
        var hasSubmittedFilters = Request.Form.Keys.Any(key =>
            key.StartsWith("filters[", StringComparison.OrdinalIgnoreCase));
        filters ??= new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!hasSubmittedFilters)
            filters = savedPreferences.ColumnFilters.ToDictionary(
                pair => pair.Key, pair => (string?)pair.Value, StringComparer.OrdinalIgnoreCase);
        if (!Request.Form.ContainsKey("sortColumn"))
            sortColumn = savedPreferences.SortColumn;
        if (!Request.Form.ContainsKey("sortDescending"))
            sortDescending = savedPreferences.SortDescending;
        if (!groupColumnsSubmitted)
            groupColumns = savedPreferences.GroupColumns;

        Dictionary<string, int> columnWidths;
        try
        {
            columnWidths = string.IsNullOrWhiteSpace(columnWidthsJson)
                ? savedPreferences.ColumnWidths
                : JsonSerializer.Deserialize<Dictionary<string, int>>(columnWidthsJson)
                    ?? savedPreferences.ColumnWidths;
        }
        catch (JsonException)
        {
            columnWidths = savedPreferences.ColumnWidths;
        }

        var selectedGridTheme = gridTheme ?? savedPreferences.Theme;
        if (selectedGridTheme is not ("midnight" or "light" or "ocean" or "contrast"))
            selectedGridTheme = "midnight";

        try
        {
            await LoadParameterOptionsAsync(report, connection, cancellationToken);
            var raw = await execution.ExecuteAsync(report, values, connection, cancellationToken);
            var validColumns = raw.Columns.Cast<DataColumn>()
                .Select(column => column.ColumnName).ToArray();

            filters = filters
                .Where(pair => validColumns.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

            groupColumns = (groupColumns ?? [])
                .Where(column => validColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            columnWidths = columnWidths
                .Where(pair => validColumns.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                .Take(200)
                .ToDictionary(pair => pair.Key, pair => Math.Clamp(pair.Value, 90, 800), StringComparer.OrdinalIgnoreCase);

            var filteredRows = raw.AsEnumerable().Where(row => filters.All(filter =>
                string.IsNullOrWhiteSpace(filter.Value) ||
                Convert.ToString(row[filter.Key], System.Globalization.CultureInfo.CurrentCulture)?
                    .Contains(filter.Value, StringComparison.CurrentCultureIgnoreCase) == true));

            var filtered = raw.Clone();
            foreach (var row in filteredRows)
                filtered.ImportRow(row);

            // Grouping is a presentation operation: sort by group keys first, then by the user's sort.
            // All column names are checked against the actual result schema before entering DataView.Sort.
            var sortTerms = new List<string>();
            foreach (var groupColumn in groupColumns)
            {
                var safeGroupColumn = groupColumn.Replace("]", "]]", StringComparison.Ordinal);
                var isActiveSortColumn = string.Equals(groupColumn, sortColumn, StringComparison.OrdinalIgnoreCase);
                var groupDirection = isActiveSortColumn && sortDescending ? "DESC" : "ASC";
                sortTerms.Add($"[{safeGroupColumn}] {groupDirection}");
            }

            if (!string.IsNullOrWhiteSpace(sortColumn) &&
                validColumns.Contains(sortColumn, StringComparer.OrdinalIgnoreCase) &&
                !groupColumns.Any(column =>
                    string.Equals(column, sortColumn, StringComparison.OrdinalIgnoreCase)))
            {
                var safeColumn = sortColumn.Replace("]", "]]", StringComparison.Ordinal);
                var direction = sortDescending ? "DESC" : "ASC";
                sortTerms.Add($"[{safeColumn}] {direction}");
            }

            if (sortTerms.Count > 0)
            {
                var view = new DataView(filtered) { Sort = string.Join(", ", sortTerms) };
                filtered = view.ToTable();
            }

            var preferences = savedPreferences;
            var totalRows = filtered.Rows.Count;
            // Pagination is controlled by ReportDefinitions, not per-user grid preferences.
            var actualPageSize = report.NeedsPagination
                ? Math.Clamp(report.PageSize > 0 ? report.PageSize : 25, 10, 5000)
                : Math.Max(1, totalRows);
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalRows / (double)actualPageSize));
            page = Math.Clamp(page, 1, totalPages);

            var paged = filtered.Clone();
            foreach (var row in filtered.AsEnumerable()
                         .Skip((page - 1) * actualPageSize)
                         .Take(actualPageSize))
                paged.ImportRow(row);

            var hidden = (hiddenColumnsSubmitted ? hiddenColumns ?? [] : preferences.HiddenColumns)
                .Where(column => validColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            await settings.SaveAsync(id, new GridPreferences
            {
                PageSize = actualPageSize,
                HiddenColumns = hidden,
                ColumnFilters = filters.Where(x => !string.IsNullOrWhiteSpace(x.Value))
                    .ToDictionary(x => x.Key, x => x.Value!, StringComparer.OrdinalIgnoreCase),
                SortColumn = sortColumn,
                SortDescending = sortDescending,
                GroupColumns = groupColumns,
                ColumnWidths = columnWidths,
                Theme = selectedGridTheme
            }, cancellationToken);

            return View("Result", new ReportResultViewModel
            {
                Report = report,
                SelectedConnection = connection,
                Values = values,
                Filters = filters,
                Result = paged,
                AllColumns = validColumns,
                HiddenColumns = hidden,
                GroupColumns = groupColumns,
                ColumnWidths = columnWidths,
                GridTheme = selectedGridTheme,
                SortColumn = sortColumn,
                SortDescending = sortDescending,
                Page = page,
                PageSize = actualPageSize,
                TotalRows = totalRows
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Report {ReportId} execution failed.", id);
            Response.StatusCode = StatusCodes.Status400BadRequest;
            var userMessage = ex is ArgumentException or InvalidOperationException or NotSupportedException
                ? ex.Message
                : "در اجرای گزارش خطایی رخ داد. تنظیمات اتصال و لاگ سرور را بررسی کنید.";

            try
            {
                await LoadParameterOptionsAsync(report, connection, cancellationToken);
            }
            catch (Exception optionsException) when (optionsException is not OperationCanceledException)
            {
                logger.LogWarning(optionsException, "Dynamic options for report {ReportId} could not be loaded.", id);
                foreach (var parameter in report.Parameters.Where(parameter =>
                             parameter.Type is "RadioButton" or "ListBox" or "ComboBox"))
                    parameter.Options ??= "[]";
            }

            return View("Run", new ReportRunViewModel
            {
                Report = report,
                SelectedConnection = connection,
                Values = values,
                PageSize = report.NeedsPagination ? Math.Clamp(report.PageSize > 0 ? report.PageSize : 25, 10, 5000) : Math.Max(1, report.PageSize),
                Error = userMessage
            });
        }
    }
}
