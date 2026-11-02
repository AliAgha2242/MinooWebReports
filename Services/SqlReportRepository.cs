using Microsoft.Data.SqlClient;
using MinooWebReports.Models;

namespace MinooWebReports.Services;

/// <summary>Reads report metadata from the separately configured metadata database.</summary>
public sealed class SqlReportRepository(IConfiguration configuration) : IReportRepository
{
    private string ConnectionString => configuration.GetConnectionString("ReportsMetadata")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:ReportsMetadata تنظیم نشده است. اتصال پایگاه داده متادیتا را در محیط اجرا تعریف کنید.");

    public async Task<IReadOnlyList<ReportDefinition>> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT Id, Name, SourceType, SqlQuery, ExcelFileName,
                   NeedsPagination, PageSize
            FROM dbo.ReportDefinitions
            WHERE IsDeleted = 0
            ORDER BY Name;
            """;

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 15 };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var reports = new List<ReportDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            reports.Add(new ReportDefinition
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                SourceType = reader.GetString(2),
                SqlQuery = reader.IsDBNull(3) ? null : reader.GetString(3),
                ExcelFileName = reader.IsDBNull(4) ? null : reader.GetString(4),
                NeedsPagination = reader.GetBoolean(5),
                PageSize = reader.IsDBNull(6) ? 25 : reader.GetInt32(6)
            });
        }

        return reports;
    }

    public async Task<ReportDefinition?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT Id, Name, SourceType, SqlQuery, ExcelFileName,
                   NeedsPagination, PageSize
            FROM dbo.ReportDefinitions
            WHERE Id = @Id AND IsDeleted = 0;
            """;

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 15 };
        command.Parameters.Add("@Id", System.Data.SqlDbType.Int).Value = id;

        ReportDefinition? report = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                report = new ReportDefinition
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    SourceType = reader.GetString(2),
                    SqlQuery = reader.IsDBNull(3) ? null : reader.GetString(3),
                    ExcelFileName = reader.IsDBNull(4) ? null : reader.GetString(4),
                    NeedsPagination = reader.GetBoolean(5),
                    PageSize = reader.GetInt32(6)
                };
            }
        }

        if (report is null)
            return null;

        const string parameterSql = """
            SELECT Id, ReportDefinitionId, Name, DisplayName, Type,
                   DataSource, DisplayNameField, ValueField, Options, SortOrder
            FROM dbo.ReportParameters
            WHERE ReportDefinitionId = @ReportDefinitionId
            ORDER BY SortOrder, Id;
            """;

        await using var parameterCommand = new SqlCommand(parameterSql, connection)
        {
            CommandTimeout = 15
        };
        parameterCommand.Parameters.Add("@ReportDefinitionId", System.Data.SqlDbType.Int).Value = id;
        await using var parameterReader = await parameterCommand.ExecuteReaderAsync(cancellationToken);
        var parameters = new List<ReportParameter>();

        while (await parameterReader.ReadAsync(cancellationToken))
        {
            parameters.Add(new ReportParameter
            {
                Id = parameterReader.GetInt32(0),
                ReportDefinitionId = parameterReader.GetInt32(1),
                Name = parameterReader.GetString(2),
                DisplayName = parameterReader.GetString(3),
                Type = parameterReader.GetString(4),
                DataSource = parameterReader.IsDBNull(5) ? null : parameterReader.GetString(5),
                DisplayNameField = parameterReader.IsDBNull(6) ? null : parameterReader.GetString(6),
                ValueField = parameterReader.IsDBNull(7) ? null : parameterReader.GetString(7),
                Options = parameterReader.IsDBNull(8) ? null : parameterReader.GetString(8),
                SortOrder = parameterReader.GetInt32(9)
            });
        }

        report.Parameters = parameters;
        return report;
    }
}
