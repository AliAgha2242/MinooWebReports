using System.Data;
using Microsoft.AspNetCore.Mvc;
using MinooWebReports.Models;
using MinooWebReports.Services;

namespace MinooWebReports.Controllers;

public sealed class ReportsController(
    IReportRepository reports,
    IReportExecutionService execution,
    ISelectedConnectionService selectedConnection,
    IUserReportSettingsService settings) : Controller
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

        if (selectedConnection.Current is null)
        {
            TempData["Error"] = "ابتدا باید محل اتصال را انتخاب کنید.";
            return RedirectToAction("Index", "Home");
        }

        return View(new ReportRunViewModel
        {
            Report = report,
            PageSize = Math.Clamp(report.PageSize, 10, 200)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(
        int id,
        Dictionary<string, string?>? values,
        Dictionary<string, string?>? filters,
        string? sortColumn,
        bool sortDescending,
        List<string>? hiddenColumns,
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

        values ??= new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        filters ??= new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var raw = await execution.ExecuteSqlAsync(report, values, connection, cancellationToken);
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

            var preferences = await settings.GetAsync(id, cancellationToken);
            var actualPageSize = Math.Clamp(pageSize ?? preferences.PageSize, 10, 200);
            var totalRows = filtered.Rows.Count;
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalRows / (double)actualPageSize));
            page = Math.Clamp(page, 1, totalPages);

            var paged = filtered.Clone();
            foreach (var row in filtered.AsEnumerable()
                         .Skip((page - 1) * actualPageSize)
                         .Take(actualPageSize))
                paged.ImportRow(row);

            var hidden = (hiddenColumns ?? preferences.HiddenColumns)
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
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View("Run", new ReportRunViewModel
            {
                Report = report,
                Values = values,
                PageSize = Math.Clamp(pageSize ?? report.PageSize, 10, 200),
                Error = ex.Message
            });
        }
    }
}
