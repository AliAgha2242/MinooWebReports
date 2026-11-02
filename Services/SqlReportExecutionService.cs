using System.Data;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using MinooWebReports.Models;

namespace MinooWebReports.Services;

/// <summary>
/// Executes parameterized SQL reports and reads stored Excel workbooks.
/// SQL target identities must have SELECT-only permissions; the text gate is defense-in-depth.
/// </summary>
public sealed class SqlReportExecutionService(
    IConnectionStringFactory connectionStringFactory,
    IConfiguration configuration) : IReportExecutionService
{
    private static readonly Regex ParameterNameRegex =
        new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] ForbiddenTokens =
    [
        "INSERT", "UPDATE", "DELETE", "MERGE", "DROP", "ALTER", "CREATE",
        "TRUNCATE", "EXEC", "EXECUTE", "GRANT", "REVOKE", "DENY", "DBCC",
        "BACKUP", "RESTORE", "BULK", "WAITFOR", "OPENROWSET", "OPENQUERY", "OPENDATASOURCE", "USE", "SHUTDOWN", "RECONFIGURE"
    ];

    public Task<DataTable> ExecuteAsync(
        ReportDefinition report,
        IReadOnlyDictionary<string, string?> values,
        VahedTejari connection,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(report.SourceType, "Sql", StringComparison.OrdinalIgnoreCase))
            return ExecuteSqlAsync(report, values, connection, cancellationToken);

        if (string.Equals(report.SourceType, "Excel", StringComparison.OrdinalIgnoreCase))
            return ExecuteExcelAsync(report, values, cancellationToken);

        throw new InvalidOperationException("نوع منبع گزارش پشتیبانی نمی‌شود.");
    }

    public async Task<string?> LoadParameterOptionsJsonAsync(
        ReportParameter parameter,
        VahedTejari connection,
        CancellationToken cancellationToken = default)
    {
        var sourceSql = parameter.DataSource;
        if (string.IsNullOrWhiteSpace(sourceSql))
            return parameter.Options;

        sourceSql = sourceSql.Trim();
        ValidateReadOnlySelect(sourceSql);
        var builder = new SqlConnectionStringBuilder(connectionStringFactory.Create(connection))
        {
            ApplicationIntent = ApplicationIntent.ReadOnly,
            ConnectTimeout = 15
        };

        await using var sqlConnection = new SqlConnection(builder.ConnectionString);
        await sqlConnection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sourceSql, sqlConnection)
        {
            CommandTimeout = 15,
            CommandType = CommandType.Text
        };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (reader.FieldCount == 0)
            return "[]";

        var valueField = string.IsNullOrWhiteSpace(parameter.ValueField)
            ? reader.GetName(0)
            : parameter.ValueField;
        var displayField = string.IsNullOrWhiteSpace(parameter.DisplayNameField)
            ? valueField
            : parameter.DisplayNameField;

        int valueOrdinal;
        int displayOrdinal;
        try
        {
            valueOrdinal = reader.GetOrdinal(valueField);
            displayOrdinal = reader.GetOrdinal(displayField);
        }
        catch (IndexOutOfRangeException)
        {
            throw new InvalidOperationException(
                $"ستون‌های ValueField یا DisplayNameField برای پارامتر «{parameter.DisplayName}» در منبع داده پیدا نشدند.");
        }

        var options = new List<object>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (options.Count >= 2000)
                throw new InvalidOperationException("تعداد گزینه‌های پارامتر از سقف مجاز ۲۰۰۰ مورد بیشتر است.");

            var value = await reader.IsDBNullAsync(valueOrdinal, cancellationToken)
                ? string.Empty
                : Convert.ToString(reader.GetValue(valueOrdinal), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            var label = await reader.IsDBNullAsync(displayOrdinal, cancellationToken)
                ? value
                : Convert.ToString(reader.GetValue(displayOrdinal), System.Globalization.CultureInfo.CurrentCulture) ?? value;

            options.Add(new { value, label });
        }

        return System.Text.Json.JsonSerializer.Serialize(options);
    }

    private async Task<DataTable> ExecuteSqlAsync(
        ReportDefinition report,
        IReadOnlyDictionary<string, string?> values,
        VahedTejari connection,
        CancellationToken cancellationToken)
    {
        var sql = (report.SqlQuery ?? string.Empty).Trim();
        ValidateReadOnlySelect(sql);

        var timeout = Math.Clamp(configuration.GetValue("Reports:CommandTimeoutSeconds", 60), 5, 180);
        var maxRows = Math.Clamp(configuration.GetValue("Reports:MaxRows", 5000), 100, 50000);

        var builder = new SqlConnectionStringBuilder(connectionStringFactory.Create(connection))
        {
            ApplicationIntent = ApplicationIntent.ReadOnly,
            ConnectTimeout = 15
        };

        await using var sqlConnection = new SqlConnection(builder.ConnectionString);
        await sqlConnection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, sqlConnection)
        {
            CommandTimeout = timeout,
            CommandType = CommandType.Text
        };

        foreach (var parameter in report.Parameters)
        {
            if (!ParameterNameRegex.IsMatch(parameter.Name))
                throw new InvalidOperationException($"نام پارامتر «{parameter.Name}» معتبر نیست.");
            if (parameter.Type is not ("Text" or "Number" or "Date" or "Checkbox" or "RadioButton" or "ListBox"))
                throw new InvalidOperationException($"نوع پارامتر «{parameter.Type}» پشتیبانی نمی‌شود.");

            values.TryGetValue(parameter.Name, out var rawValue);
            var sqlParameter = command.Parameters.Add("@" + parameter.Name, GetSqlDbType(parameter.Type));
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                sqlParameter.Value = parameter.Type == "Checkbox" ? false : DBNull.Value;
                continue;
            }

            sqlParameter.Value = ConvertValue(parameter, rawValue);
        }

        var table = new DataTable();
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SequentialAccess, cancellationToken);

        for (var i = 0; i < reader.FieldCount; i++)
            table.Columns.Add(reader.GetName(i), Nullable.GetUnderlyingType(reader.GetFieldType(i))
                ?? reader.GetFieldType(i));

        while (await reader.ReadAsync(cancellationToken))
        {
            if (table.Rows.Count >= maxRows)
                throw new InvalidOperationException(
                    $"تعداد رکوردهای خروجی از سقف مجاز ({maxRows:N0}) بیشتر است. لطفاً پارامترهای گزارش را محدودتر کنید.");

            var row = table.NewRow();
            for (var i = 0; i < reader.FieldCount; i++)
                row[i] = await reader.IsDBNullAsync(i, cancellationToken)
                    ? DBNull.Value
                    : reader.GetValue(i);
            table.Rows.Add(row);
        }

        return table;
    }

    private async Task<DataTable> ExecuteExcelAsync(
        ReportDefinition report,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var content = report.ExcelContent;
        if (content is null || content.Length == 0)
            throw new InvalidOperationException("محتوای فایل Excel برای این گزارش ثبت نشده است.");

        var maxBytes = Math.Clamp(configuration.GetValue("Reports:MaxExcelBytes", 20 * 1024 * 1024),
            1024 * 1024, 100 * 1024 * 1024);
        if (content.Length > maxBytes)
            throw new InvalidOperationException("حجم فایل Excel از سقف مجاز بیشتر است.");

        using var stream = new MemoryStream(content, writable: false);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("برگه‌ای در فایل Excel پیدا نشد.");
        var usedRange = worksheet.RangeUsed();

        var table = new DataTable();
        if (usedRange is null)
            return table;

        var firstRow = usedRange.RangeAddress.FirstAddress.RowNumber;
        var lastRow = usedRange.RangeAddress.LastAddress.RowNumber;
        var firstColumn = usedRange.RangeAddress.FirstAddress.ColumnNumber;
        var lastColumn = usedRange.RangeAddress.LastAddress.ColumnNumber;

        for (var column = firstColumn; column <= lastColumn; column++)
        {
            var header = worksheet.Cell(firstRow, column).GetString().Trim();
            if (string.IsNullOrWhiteSpace(header))
                header = $"Column{column - firstColumn + 1}";

            var uniqueHeader = header;
            var suffix = 2;
            while (table.Columns.Contains(uniqueHeader))
                uniqueHeader = $"{header}_{suffix++}";

            table.Columns.Add(uniqueHeader, typeof(string));
        }

        foreach (var parameter in report.Parameters)
        {
            if (!values.TryGetValue(parameter.Name, out var parameterValue) ||
                string.IsNullOrWhiteSpace(parameterValue))
                continue;

            var matchingColumn = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(column => string.Equals(column.ColumnName, parameter.Name,
                    StringComparison.OrdinalIgnoreCase));

            if (matchingColumn is null)
                throw new InvalidOperationException(
                    $"برای پارامتر «{parameter.DisplayName}» ستونی با نام «{parameter.Name}» در فایل Excel پیدا نشد.");
        }

        var maxRows = Math.Clamp(configuration.GetValue("Reports:MaxRows", 5000), 100, 50000);
        for (var rowNumber = firstRow + 1; rowNumber <= lastRow; rowNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = table.NewRow();
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var cell = worksheet.Cell(rowNumber, column);
                row[column - firstColumn] = cell.IsEmpty() ? DBNull.Value : cell.GetString();
            }

            var matchesParameters = report.Parameters.All(parameter =>
            {
                values.TryGetValue(parameter.Name, out var parameterValue);
                if (parameter.Type == "Checkbox" && string.IsNullOrWhiteSpace(parameterValue))
                    parameterValue = "false";
                if (string.IsNullOrWhiteSpace(parameterValue))
                    return true;

                var column = table.Columns[parameter.Name]!;
                var cellValue = Convert.ToString(row[column], System.Globalization.CultureInfo.InvariantCulture);
                IEnumerable<string> requestedValues = parameter.Type == "ListBox"
                    ? parameterValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : new[] { parameterValue };
                return requestedValues.Contains(cellValue ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            });

            if (!matchesParameters)
                continue;

            if (table.Rows.Count >= maxRows)
                throw new InvalidOperationException(
                    $"تعداد رکوردهای Excel از سقف مجاز ({maxRows:N0}) بیشتر است.");

            table.Rows.Add(row);
        }

        return table;
    }

    private static void ValidateReadOnlySelect(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql) ||
            !sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("فقط دستور SELECT برای گزارش مجاز است.");

        // Accept a single plain SELECT only. SQL target accounts must still be read-only.
        if (sql.Contains(';') || sql.Contains("--", StringComparison.Ordinal) ||
            sql.Contains("/*", StringComparison.Ordinal) || sql.Contains("*/", StringComparison.Ordinal))
            throw new InvalidOperationException("گزارش باید فقط شامل یک دستور SELECT بدون comment باشد.");

        var tokens = Regex.Matches(sql, @"\b[A-Za-z_]+\b")
            .Cast<Match>()
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var forbidden = ForbiddenTokens.FirstOrDefault(tokens.Contains);
        if (forbidden is not null)
            throw new InvalidOperationException($"عبارت {forbidden} در متن گزارش مجاز نیست.");

        if (tokens.Contains("INTO") || tokens.Contains("OPENXML") ||
            tokens.Any(token => token.StartsWith("XP_", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("این نوع عبارت در گزارش فقط‌خواندنی مجاز نیست.");
    }

    private static SqlDbType GetSqlDbType(string type) => type switch
    {
        "Number" => SqlDbType.Decimal,
        "Date" => SqlDbType.Date,
        "Checkbox" => SqlDbType.Bit,
        _ => SqlDbType.NVarChar
    };

    private static object ConvertValue(ReportParameter parameter, string value) =>
        parameter.Type switch
        {
            "Number" when decimal.TryParse(value, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var number) => number,
            "Number" => throw new ArgumentException($"مقدار عددی «{parameter.DisplayName}» معتبر نیست."),
            "Date" when DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date) => date.Date,
            "Date" => throw new ArgumentException($"تاریخ «{parameter.DisplayName}» معتبر نیست."),
            "Checkbox" when bool.TryParse(value, out var flag) => flag,
            "Checkbox" when value == "1" => true,
            "Checkbox" when value == "0" => false,
            "Checkbox" => throw new ArgumentException($"مقدار «{parameter.DisplayName}» معتبر نیست."),
            _ => value.Trim()
        };
}
