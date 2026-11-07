using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MinooWebReports.Models;
using MinooWebReports.Services;

namespace MinooWebReports.Controllers;

[Authorize]
public sealed class AdminReportsController(
    IReportRepository reports,
    IConfiguration configuration,
    ILogger<AdminReportsController> logger) : Controller
{
    private bool IsReportsAdmin()
    {
        var configuredCodes = configuration.GetSection("Reports:AdminUserCodes").Get<string[]>() ?? [];
        var singleSetting = configuration["Reports:AdminUserCodes"];
        var allowedCodes = configuredCodes
            .Concat((singleSetting ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var userCode = User.FindFirst("UserCode")?.Value;
        return !string.IsNullOrWhiteSpace(userCode) && allowedCodes.Contains(userCode);
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!IsReportsAdmin())
            return Forbid();

        var definitions = await reports.GetActiveAsync(cancellationToken);
        var detailedReports = new List<ReportDefinition>(definitions.Count);
        foreach (var definition in definitions)
        {
            var detailed = await reports.GetByIdAsync(definition.Id, cancellationToken);
            if (detailed is not null)
                detailedReports.Add(detailed);
        }

        return View(new ReportParameterAdminViewModel { Reports = detailedReports });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDefault(
        int reportId,
        int parameterId,
        string? defaultValue,
        CancellationToken cancellationToken)
    {
        if (!IsReportsAdmin())
            return Forbid();

        var report = await reports.GetByIdAsync(reportId, cancellationToken);
        var parameter = report?.Parameters.SingleOrDefault(item => item.Id == parameterId);
        if (parameter is null)
            return NotFound();

        defaultValue = string.IsNullOrWhiteSpace(defaultValue) ? null : defaultValue.Trim();
        var validationError = ValidateDefault(parameter, defaultValue);
        if (validationError is not null)
        {
            TempData["Error"] = validationError;
            return RedirectToAction(nameof(Index));
        }

        var connectionString = configuration.GetConnectionString("ReportsMetadata");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:ReportsMetadata تنظیم نشده است.");

        const string sql = """
            UPDATE p
            SET DefaultValue = @DefaultValue
            FROM dbo.ReportParameters AS p
            INNER JOIN dbo.ReportDefinitions AS d ON d.Id = p.ReportDefinitionId
            WHERE p.Id = @ParameterId
              AND p.ReportDefinitionId = @ReportId
              AND d.IsDeleted = 0;
            """;

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 15 };
            command.Parameters.Add("@DefaultValue", System.Data.SqlDbType.NVarChar, -1).Value =
                (object?)defaultValue ?? DBNull.Value;
            command.Parameters.Add("@ParameterId", System.Data.SqlDbType.Int).Value = parameterId;
            command.Parameters.Add("@ReportId", System.Data.SqlDbType.Int).Value = reportId;
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected == 0)
                return NotFound();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not save default value for report parameter {ParameterId}.", parameterId);
            TempData["Error"] = "ذخیره مقدار پیش‌فرض انجام نشد. اتصال پایگاه داده متادیتا را بررسی کنید.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = $"مقدار پیش‌فرض پارامتر «{parameter.DisplayName}» ذخیره شد.";
        return RedirectToAction(nameof(Index));
    }

    private static string? ValidateDefault(ReportParameter parameter, string? value)
    {
        if (value is null)
            return null;

        if (value.Length > 4000)
            return "مقدار پیش‌فرض نباید بیشتر از ۴۰۰۰ کاراکتر باشد.";

        switch (parameter.Type)
        {
            case "Number":
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                    return $"مقدار پیش‌فرض پارامتر «{parameter.DisplayName}» باید عدد صحیح یا اعشاری معتبر باشد.";
                break;

            case "Checkbox":
                if (!bool.TryParse(value, out _))
                    return $"مقدار پیش‌فرض پارامتر «{parameter.DisplayName}» باید true یا false باشد.";
                break;

            case "ListBox":
                try
                {
                    using var document = JsonDocument.Parse(value);
                    if (document.RootElement.ValueKind != JsonValueKind.Array ||
                        document.RootElement.GetArrayLength() > 2000)
                        return "مقدار پیش‌فرض لیست‌باکس باید آرایه JSON با حداکثر ۲۰۰۰ عضو باشد.";

                    foreach (var item in document.RootElement.EnumerateArray())
                    {
                        var numericValue = item.ValueKind == JsonValueKind.String
                            ? item.GetString()
                            : item.ValueKind == JsonValueKind.Number ? item.ToString() : null;
                        if (!decimal.TryParse(numericValue, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                            return "مقادیر پیش‌فرض لیست‌باکس فقط باید عدد صحیح یا اعشاری باشند؛ مانند [12, 14.5].";
                    }
                }
                catch (JsonException)
                {
                    return "مقدار پیش‌فرض لیست‌باکس باید JSON معتبر باشد؛ مانند [12, 14.5].";
                }
                break;
        }

        return null;
    }
}
