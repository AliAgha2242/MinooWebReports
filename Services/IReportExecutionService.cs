using System.Data;
using MinooWebReports.Models;

namespace MinooWebReports.Services;

public interface IReportExecutionService
{
    Task<DataTable> ExecuteAsync(
        ReportDefinition report,
        IReadOnlyDictionary<string, string?> values,
        VahedTejari connection,
        CancellationToken cancellationToken = default);

    Task<string?> LoadParameterOptionsJsonAsync(
        ReportParameter parameter,
        VahedTejari connection,
        CancellationToken cancellationToken = default);
}
