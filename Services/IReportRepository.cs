using MinooWebReports.Models;

namespace MinooWebReports.Services;

public interface IReportRepository
{
    Task<IReadOnlyList<ReportDefinition>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<ReportDefinition?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
}
