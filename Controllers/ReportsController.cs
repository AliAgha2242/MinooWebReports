using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
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
    IMultiServerReportQueryService multiServerQuery,
    ISelectedConnectionService selectedConnection,
    ICompanyAppLocationService companyLocations,
    IUserReportSettingsService settings,
    IMemoryCache reportCache,
    ILogger<ReportsController> logger,
    IConfiguration configuration) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewBag.IsReportsAdmin = IsReportsAdmin();
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

        var executionConnections = await GetAuthorizedReportConnectionsAsync(report, cancellationToken);
        if (executionConnections.Count == 0)
        {
            TempData["Error"] = "برای اجرای این گزارش، محل اجرای پیش‌فرض را انتخاب کنید یا مطمئن شوید به تمام سرورهای تعریف‌شده دسترسی دارید.";
            return RedirectToAction("Index", "Home");
        }

        var connection = executionConnections[0];
        if (report.UseDefaultServer)
            await selectedConnection.SelectAsync(connection, cancellationToken);
        var preferences = await settings.GetAsync(id, cancellationToken);
        string? optionsError = null;
        try
        {
            await LoadParameterOptionsAsync(report, executionConnections, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Parameter options for report {ReportId} could not be loaded.", id);
            optionsError = "دریافت گزینه‌های پارامترهای گزارش با خطا مواجه شد. تنظیمات منبع گزینه‌ها را بررسی کنید.";
            foreach (var parameter in report.Parameters.Where(parameter =>
                         parameter.Type is "RadioButton" or "ListBox" or "ComboBox"))
                parameter.Options ??= "[]";
        }

        var initialValues = report.Parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.DefaultValue))
            .ToDictionary(parameter => parameter.Name, parameter => GetDisplayDefault(parameter), StringComparer.OrdinalIgnoreCase);

        return View(new ReportRunViewModel
        {
            Report = report,
            Values = initialValues,
            SelectedConnection = connection,
            ExecutionConnections = executionConnections,
            PageSize = report.NeedsPagination ? Math.Clamp(report.PageSize > 0 ? report.PageSize : 25, 1, 5000) : Math.Max(1, report.PageSize),
            Error = optionsError
        });
    }

    private bool IsReportsAdmin()
    {
        var configuredCodes = configuration.GetSection("Reports:AdminUserCodes").Get<string[]>() ?? [];
        var singleSetting = configuration["Reports:AdminUserCodes"];
        var allowedCodes = configuredCodes
            .Concat((singleSetting ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var userCode = User.FindFirstValue("UserCode");
        return !string.IsNullOrWhiteSpace(userCode) && allowedCodes.Contains(userCode);
    }

    private static string GetDisplayDefault(ReportParameter parameter)
    {
        if (parameter.Type != "ListBox" || string.IsNullOrWhiteSpace(parameter.DefaultValue))
            return parameter.DefaultValue ?? string.Empty;

        try
        {
            using var document = JsonDocument.Parse(parameter.DefaultValue);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return string.Empty;

            return string.Join(",", document.RootElement.EnumerateArray()
                .Select(value => value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : value.ToString())
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        }
        catch (JsonException)
        {
            return string.Empty;
        }
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

    private async Task<IReadOnlyList<VahedTejari>> GetAuthorizedReportConnectionsAsync(
        ReportDefinition report,
        CancellationToken cancellationToken)
    {
        if (report.UseDefaultServer)
        {
            var defaultConnection = await GetAuthorizedSelectedConnectionAsync(cancellationToken);
            return defaultConnection is null ? [] : [defaultConnection];
        }

        var userCode = User.FindFirstValue("UserCode");
        if (string.IsNullOrWhiteSpace(userCode) || report.ServerVahedeTejariSNs.Count == 0)
            return [];

        var authorized = await companyLocations.GetAllLocationAsync(new UserModel
        {
            UserCode = userCode,
            UserName = User.Identity?.Name ?? string.Empty
        }, cancellationToken);

        var authorizedById = authorized
            .GroupBy(item => item.VahedetejariSn)
            .ToDictionary(group => group.Key, group => group.First());
        var requestedIds = report.ServerVahedeTejariSNs.Distinct().ToArray();

        // Do not silently return partial results if the user cannot access one of the configured servers.
        if (requestedIds.Length != report.ServerVahedeTejariSNs.Count ||
            requestedIds.Any(id => !authorizedById.ContainsKey(id)))
            return [];

        return requestedIds.Select(id => authorizedById[id]).ToArray();
    }

    private async Task<DataTable> ExecuteReportDataAsync(
        ReportDefinition report,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyList<VahedTejari> connections,
        CancellationToken cancellationToken)
    {
        if (report.UseDefaultServer ||
            !string.Equals(report.SourceType, "Sql", StringComparison.OrdinalIgnoreCase))
        {
            return await execution.ExecuteAsync(report, values, connections[0], cancellationToken);
        }

        var serverResults = await multiServerQuery.ExecuteAsync(
            report, values, connections, cancellationToken);

        var requestedIds = connections.Select(item => item.VahedetejariSn).ToHashSet();
        var returnedIds = serverResults.Select(item => item.Server.VahedetejariSn).ToArray();
        if (serverResults.Count != requestedIds.Count ||
            returnedIds.Distinct().Count() != requestedIds.Count ||
            returnedIds.Any(id => !requestedIds.Contains(id)))
        {
            throw new InvalidOperationException("اجرای گزارش روی تمام سرورهای انتخاب‌شده کامل نشد؛ نتیجه ناقص نمایش داده نمی‌شود.");
        }

        return MergeServerResults(serverResults);
    }

    private async Task<Dictionary<string, string>> LoadColumnDisplayNamesAsync(
        IReadOnlyList<VahedTejari> connections,
        CancellationToken cancellationToken)
    {
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var connection in connections)
        {
            var cacheKey = $"report-column-labels:{connection.VahedetejariSn}:{connection.DataBAset}";
            var serverMappings = await reportCache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
                try
                {
                    var loaded = await execution.LoadColumnDisplayNamesAsync(connection, cancellationToken);
                    return new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex,
                        "Persian column labels could not be loaded from paObjectHa for server {ServerId}.",
                        connection.VahedetejariSn);
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
            });

            foreach (var pair in serverMappings ?? new Dictionary<string, string>())
                mappings.TryAdd(pair.Key, pair.Value);
        }

        return mappings;
    }

    private static (DataTable Table, Dictionary<string, string> NumericSortColumns) CreateSortableTable(
        DataTable source)
    {
        var sortable = source.Copy();
        var numericSortColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var numberStyles = System.Globalization.NumberStyles.Number |
                           System.Globalization.NumberStyles.AllowExponent;

        foreach (DataColumn sourceColumn in source.Columns)
        {
            if (sourceColumn.DataType != typeof(string) || source.Rows.Count == 0)
                continue;

            var parsedValues = new decimal?[source.Rows.Count];
            var hasNumericValue = false;
            var isNumericColumn = true;

            for (var rowIndex = 0; rowIndex < source.Rows.Count; rowIndex++)
            {
                var rawValue = source.Rows[rowIndex][sourceColumn];
                if (rawValue is DBNull || string.IsNullOrWhiteSpace(Convert.ToString(rawValue)))
                    continue;

                var textValue = Convert.ToString(rawValue, System.Globalization.CultureInfo.InvariantCulture);
                if (!decimal.TryParse(textValue, numberStyles,
                        System.Globalization.CultureInfo.InvariantCulture, out var numericValue))
                {
                    isNumericColumn = false;
                    break;
                }

                parsedValues[rowIndex] = numericValue;
                hasNumericValue = true;
            }

            if (!isNumericColumn || !hasNumericValue)
                continue;

            var sortColumnName = $"__numeric_sort_{numericSortColumns.Count}";
            while (sortable.Columns.Contains(sortColumnName))
                sortColumnName += "_";

            var sortColumn = sortable.Columns.Add(sortColumnName, typeof(decimal));
            for (var rowIndex = 0; rowIndex < parsedValues.Length; rowIndex++)
            {
                if (parsedValues[rowIndex].HasValue)
                    sortable.Rows[rowIndex][sortColumn] = parsedValues[rowIndex]!.Value;
            }

            numericSortColumns[sourceColumn.ColumnName] = sortColumnName;
        }

        return (sortable, numericSortColumns);
    }

    private static DataTable MergeServerResults(
        IReadOnlyList<ServerReportExecutionResult> serverResults)
    {
        var merged = new DataTable();
        var columnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in serverResults)
        {
            foreach (DataColumn column in result.Data.Columns)
            {
                if (columnNames.Add(column.ColumnName))
                    merged.Columns.Add(column.ColumnName, typeof(string));
            }
        }

        foreach (var result in serverResults)
        {
            foreach (DataRow sourceRow in result.Data.Rows)
            {
                var targetRow = merged.NewRow();
                foreach (DataColumn sourceColumn in result.Data.Columns)
                {
                    var value = sourceRow[sourceColumn];
                    targetRow[sourceColumn.ColumnName] = value is DBNull
                        ? DBNull.Value
                        : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                }

                merged.Rows.Add(targetRow);
            }
        }

        return merged;
    }

    private async Task LoadParameterOptionsAsync(
        ReportDefinition report,
        IReadOnlyList<VahedTejari> connections,
        CancellationToken cancellationToken)
    {
        if (connections.Count == 0)
            throw new InvalidOperationException("برای دریافت گزینه‌های پارامتر، سروری انتخاب نشده است.");

        foreach (var parameter in report.Parameters.Where(parameter =>
                     parameter.Type is "RadioButton" or "ListBox" or "ComboBox"))
        {
            // Static options are identical for every server. Dynamic option queries,
            // however, must run on every selected server so the user can choose values
            // that exist outside the first server as well.
            if (string.IsNullOrWhiteSpace(parameter.DataSource))
            {
                parameter.Options = await execution.LoadParameterOptionsJsonAsync(
                    parameter, connections[0], cancellationToken);
                continue;
            }

            var optionResults = await Task.WhenAll(connections.Select(connection =>
                execution.LoadParameterOptionsJsonAsync(parameter, connection, cancellationToken)));

            parameter.Options = MergeParameterOptions(optionResults);
        }
    }

    private static string MergeParameterOptions(IEnumerable<string?> optionJsonResults)
    {
        var merged = new List<System.Text.Json.JsonElement>();
        var seenValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var json in optionJsonResults)
        {
            if (string.IsNullOrWhiteSpace(json))
                continue;

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("ساختار گزینه‌های پارامتر معتبر نیست.");

            foreach (var option in document.RootElement.EnumerateArray())
            {
                var key = option.ValueKind == System.Text.Json.JsonValueKind.Object &&
                          option.TryGetProperty("value", out var valueElement)
                    ? valueElement.ToString()
                    : option.ToString();

                if (seenValues.Add(key))
                    merged.Add(option.Clone());
            }
        }

        return JsonSerializer.Serialize(merged);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(
        int id,
        Dictionary<string, string[]>? submittedValues,
        Dictionary<string, string>? submittedListBoxes,
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

        var executionConnections = await GetAuthorizedReportConnectionsAsync(report, cancellationToken);
        if (executionConnections.Count == 0)
        {
            TempData["Error"] = "برای اجرای این گزارش، محل اجرای پیش‌فرض را انتخاب کنید یا مطمئن شوید به تمام سرورهای تعریف‌شده دسترسی دارید.";
            return RedirectToAction("Index", "Home");
        }

        var connection = executionConnections[0];
        if (report.UseDefaultServer)
            await selectedConnection.SelectAsync(connection, cancellationToken);
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in submittedValues ?? new Dictionary<string, string[]>())
        {
            var parameter = report.Parameters.FirstOrDefault(item =>
                string.Equals(item.Name, pair.Key, StringComparison.OrdinalIgnoreCase));
            values[pair.Key] = parameter?.Type == "Checkbox"
                ? pair.Value?.LastOrDefault()
                : string.Join(",", pair.Value ?? []);
        }

        foreach (var parameter in report.Parameters)
        {
            var listBoxWasSubmitted = parameter.Type == "ListBox" &&
                submittedListBoxes?.ContainsKey(parameter.Name) == true;
            if (!listBoxWasSubmitted &&
                string.IsNullOrWhiteSpace(values.GetValueOrDefault(parameter.Name)) &&
                !string.IsNullOrWhiteSpace(parameter.DefaultValue))
            {
                values[parameter.Name] = GetDisplayDefault(parameter);
            }
        }
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
            // Parameter options are only needed on the Run page, not while paging an existing result.
            // Cache the raw report rows briefly per user, report, connection and parameter set.
            var cachePayload = JsonSerializer.Serialize(new
            {
                report.Id,
                report.SourceType,
                report.SqlQuery,
                report.ExcelFileName,
                ExcelContentHash = report.ExcelContent is null
                    ? null
                    : Convert.ToHexString(SHA256.HashData(report.ExcelContent)),
                UseDefaultServer = report.UseDefaultServer,
                ConfiguredServerIds = report.ServerVahedeTejariSNs.OrderBy(x => x).ToArray(),
                Connections = executionConnections.Select(item => new
                {
                    item.VahedetejariSn,
                    item.Server,
                    item.ServerIp,
                    item.DataBAset
                }).ToArray(),
                User = User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? User.FindFirstValue("UserCode")
                    ?? User.Identity?.Name
                    ?? HttpContext.Session.Id,
                Values = values.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).ToArray()
            });
            var cacheKey = "report-result:" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(cachePayload)));
            DataTable raw;
            if (Request.Form.ContainsKey("page"))
            {
                raw = await reportCache.GetOrCreateAsync(cacheKey, async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(45);
                    entry.Priority = CacheItemPriority.Normal;
                    return await ExecuteReportDataAsync(report, values, executionConnections, cancellationToken);
                }) ?? throw new InvalidOperationException("نتیجه گزارش در دسترس نیست.");
            }
            else
            {
                // A fresh Run submission must always execute the report again.
                raw = await ExecuteReportDataAsync(report, values, executionConnections, cancellationToken);
                reportCache.Set(cacheKey, raw, TimeSpan.FromSeconds(45));
            }
            var columnDisplayNames = await LoadColumnDisplayNamesAsync(executionConnections, cancellationToken);
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

            // Filter and sort through DataView indexes instead of cloning the full DataTable
            // multiple times. This keeps only the requested page in a second DataTable.
            var sortable = CreateSortableTable(raw);
            var dataView = new DataView(sortable.Table);
            var filterTerms = filters
                .Where(filter => !string.IsNullOrWhiteSpace(filter.Value))
                .Select(filter =>
                {
                    var column = filter.Key.Replace("]", "]]", StringComparison.Ordinal);
                    var value = filter.Value!
                        .Replace("'", "''", StringComparison.Ordinal)
                        .Replace("[", "[[]", StringComparison.Ordinal)
                        .Replace("]", "[]]", StringComparison.Ordinal)
                        .Replace("*", "[*]", StringComparison.Ordinal)
                        .Replace("%", "[%]", StringComparison.Ordinal);
                    return $"[{column}] LIKE '%{value}%'";
                })
                .ToArray();
            if (filterTerms.Length > 0)
                dataView.RowFilter = string.Join(" AND ", filterTerms);

            // Grouping is a presentation operation: sort by group keys first, then by the user's sort.
            // All column names are checked against the actual result schema before entering DataView.Sort.
            var sortTerms = new List<string>();
            foreach (var groupColumn in groupColumns)
            {
                var safeGroupColumn = groupColumn.Replace("]", "]]", StringComparison.Ordinal);
                var isActiveSortColumn = string.Equals(groupColumn, sortColumn, StringComparison.OrdinalIgnoreCase);
                var groupDirection = isActiveSortColumn && sortDescending ? "DESC" : "ASC";
                var groupSortExpression = sortable.NumericSortColumns.TryGetValue(groupColumn, out var numericGroupColumn)
                    ? $"[{numericGroupColumn}]"
                    : $"[{safeGroupColumn}]";
                sortTerms.Add($"{groupSortExpression} {groupDirection}");
            }

            if (!string.IsNullOrWhiteSpace(sortColumn) &&
                validColumns.Contains(sortColumn, StringComparer.OrdinalIgnoreCase) &&
                !groupColumns.Any(column =>
                    string.Equals(column, sortColumn, StringComparison.OrdinalIgnoreCase)))
            {
                var safeColumn = sortColumn.Replace("]", "]]", StringComparison.Ordinal);
                var direction = sortDescending ? "DESC" : "ASC";
                var sortExpression = sortable.NumericSortColumns.TryGetValue(sortColumn, out var numericSortColumn)
                    ? $"[{numericSortColumn}]"
                    : $"[{safeColumn}]";
                sortTerms.Add($"{sortExpression} {direction}");
            }

            if (sortTerms.Count > 0)
                dataView.Sort = string.Join(", ", sortTerms);

            var preferences = savedPreferences;
            var totalRows = dataView.Count;
            // Pagination is controlled by ReportDefinitions, not per-user grid preferences.
            var actualPageSize = report.NeedsPagination
                ? Math.Clamp(report.PageSize > 0 ? report.PageSize : 25, 1, 5000)
                : Math.Max(1, totalRows);
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalRows / (double)actualPageSize));
            page = Math.Clamp(page, 1, totalPages);

            var paged = raw.Clone();
            var firstRowIndex = (page - 1) * actualPageSize;
            var lastRowIndex = Math.Min(firstRowIndex + actualPageSize, totalRows);
            for (var rowIndex = firstRowIndex; rowIndex < lastRowIndex; rowIndex++)
            {
                var sourceRowIndex = sortable.Table.Rows.IndexOf(dataView[rowIndex].Row);
                paged.ImportRow(raw.Rows[sourceRowIndex]);
            }

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
                ExecutionConnections = executionConnections,
                Values = values,
                Filters = filters,
                Result = paged,
                AllColumns = validColumns,
                ColumnDisplayNames = columnDisplayNames,
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
                await LoadParameterOptionsAsync(report, executionConnections, cancellationToken);
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
                ExecutionConnections = executionConnections,
                Values = values,
                PageSize = report.NeedsPagination ? Math.Clamp(report.PageSize > 0 ? report.PageSize : 25, 1, 5000) : Math.Max(1, report.PageSize),
                Error = userMessage
            });
        }
    }
}
