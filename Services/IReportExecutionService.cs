using System.Data;
using MinooWebReports.Models;

namespace MinooWebReports.Services;

public interface IReportExecutionService
{
    Task<DataTable> ExecuteSqlAsync(
        ReportDefinition report,
        IReadOnlyDictionary<string, string?> values,
        VahedTejari connection,
        CancellationToken cancellationToken = default);
}
