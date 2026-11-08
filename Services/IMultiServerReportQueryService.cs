using System.Data;
using MinooWebReports.Models;

namespace MinooWebReports.Services;

/// <summary>
/// Executes a SQL report on each requested server. Implement this service with the
/// multi-server query logic; the controller handles authorization and result merging.
/// </summary>
public interface IMultiServerReportQueryService
{
    Task<IReadOnlyList<ServerReportExecutionResult>> ExecuteAsync(
        ReportDefinition report,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyList<VahedTejari> servers,
        CancellationToken cancellationToken = default);
}

public sealed record ServerReportExecutionResult(VahedTejari Server, DataTable Data);
