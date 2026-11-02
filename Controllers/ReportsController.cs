using System.Data;
using Microsoft.AspNetCore.Mvc;
using MinooWebReports.Models;
using MinooWebReports.Services;

namespace MinooWebReports.Controllers;

public sealed class ReportsController(
    IReportRepository reports,
    IReportExecutionService execution,
    ISelectedConnectionService selectedConnection,
    IUserReportSettingsService settings,
    ILogger<ReportsController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = new ReportsIndexViewModel
        {
            Reports = await reports.GetActiveAsync(cancellationToken),
            SelectedConnection = selectedConnection.Current
        };
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Run(int id, CancellationToken cancellationToken)
    {
        var report = await reports.GetByIdAsync(id, cancellationToken);
        if (report is null)
            return NotFound();

        var connection = selectedConnection.Current;
        if (connection is null)
        {
            TempData["Error"] = "ابتدا باید محل اتصال را انتخاب کنید.";
            return RedirectToAction("Index", "Home");
        }

        await LoadParameterOptionsAsync(report, connection, cancellationToken);
        var preferences = await settings.GetAsync(id, cancellationToken);
        return View(new ReportRunViewModel
        {
            Report = report,
            PageSize = Math.Clamp(preferences.PageSize, 10, 200)
        });
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
        bool hiddenColumnsSubmitted = false,
        int page = 1,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var report = await reports.GetByIdAsync(id, cancellationToken);
        if (report is null)
            return NotFound();

        var connection = selectedConnection.Current;
        if (connection is null)
        {
            TempData["Error"] = "محل اتصال فعال نیست. ابتدا یک محل اتصال انتخاب کنید.";
            return RedirectToAction("Index", "Home");
        }

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

        try
        {
            await LoadParameterOptionsAsync(report, connection, cancellationToken);
            var raw = await execution.ExecuteAsync(report, values, connection, cancellationToken);
            var validColumns = raw.Columns.Cast<DataColumn>()
                .Select(column => column.ColumnName).ToArray();

            filters = filters
                .Where(pair => validColumns.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

            var filteredRows = raw.AsEnumerable().Where(row => filters.All(filter =>
                string.IsNullOrWhiteSpace(filter.Value) ||
                Convert.ToString(row[filter.Key], System.Globalization.CultureInfo.CurrentCulture)?
                    .Contains(filter.Value, StringComparison.CurrentCultureIgnoreCase) == true));

            var filtered = raw.Clone();
            foreach (var row in filteredRows)
                filtered.ImportRow(row);

            if (!string.IsNullOrWhiteSpace(sortColumn) &&
                validColumns.Contains(sortColumn, StringComparer.OrdinalIgnoreCase))
            {
                var safeColumn = sortColumn.Replace("]", "]]", StringComparison.Ordinal);
                var view = new DataView(filtered)
                {
                    Sort = $"[{safeColumn}] {(sortDescending ? "DESC" : "ASC")}"
                };
                filtered = view.ToTable();
            }

            var preferences = savedPreferences;
            var totalRows = filtered.Rows.Count;
            var actualPageSize = report.NeedsPagination
                ? Math.Clamp(pageSize ?? preferences.PageSize, 10, 200)
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
                SortDescending = sortDescending
            }, cancellationToken);

            return View("Result", new ReportResultViewModel
            {
                Report = report,
                Values = values,
                Filters = filters,
                Result = paged,
                AllColumns = validColumns,
                HiddenColumns = hidden,
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
                Values = values,
                PageSize = Math.Clamp(pageSize ?? report.PageSize, 10, 200),
                Error = userMessage
            });
        }
    }
}
