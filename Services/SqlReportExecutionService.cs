using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using MinooWebReports.Models;

namespace MinooWebReports.Services;

/// <summary>
/// Executes parameterized SELECT reports with strict row/time limits.
/// Production SQL credentials must be read-only; ApplicationIntent is not a security boundary.
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
        "BACKUP", "RESTORE", "BULK", "WAITFOR", "OPENROWSET", "OPENDATASOURCE"
    ];

    public async Task<DataTable> ExecuteSqlAsync(
        ReportDefinition report,
        IReadOnlyDictionary<string, string?> values,
        VahedTejari connection,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(report.SourceType, "Sql", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("اجرای گزارش‌های Excel در مرحله بعدی فعال می‌شود.");

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

            values.TryGetValue(parameter.Name, out var rawValue);
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                if (parameter.IsRequired)
                    throw new ArgumentException($"مقدار «{parameter.DisplayName}» الزامی است.");

                command.Parameters.AddWithValue("@" + parameter.Name, DBNull.Value);
                continue;
            }

            command.Parameters.Add("@" + parameter.Name, GetSqlDbType(parameter.Type)).Value =
                ConvertValue(parameter, rawValue);
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

    private static void ValidateReadOnlySelect(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql) ||
            !sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("فقط دستور SELECT برای گزارش مجاز است.");

        // This deliberately accepts a single, plain SELECT statement only.
        // Database-level read-only permissions remain mandatory for defense in depth.
        if (sql.Contains(';') || sql.Contains("--", StringComparison.Ordinal) ||
            sql.Contains("/*", StringComparison.Ordinal) || sql.Contains("*/", StringComparison.Ordinal))
            throw new InvalidOperationException("گزارش باید فقط شامل یک دستور SELECT بدون comment باشد.");

        var tokens = Regex.Matches(sql, @"\b[A-Za-z_]+\b")
            .Select(m => m.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var forbidden = ForbiddenTokens.FirstOrDefault(tokens.Contains);
        if (forbidden is not null)
            throw new InvalidOperationException($"عبارت {forbidden} در متن گزارش مجاز نیست.");

        if (tokens.Contains("INTO") || tokens.Contains("OPENXML") || tokens.Contains("OPENDATASOURCE"))
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
