using MinooWebReports.Models;

namespace MinooWebReports.Services;

/// <summary>
/// Temporary implementation to keep the application resolvable until the real
/// multi-server query service is supplied.
/// Replace this registration in Program.cs when implementing the service.
/// </summary>
public sealed class NotConfiguredMultiServerReportQueryService : IMultiServerReportQueryService
{
    public Task<IReadOnlyList<ServerReportExecutionResult>> ExecuteAsync(
        ReportDefinition report,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyList<VahedTejari> servers,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "سرویس اجرای Query چندسروره هنوز پیاده‌سازی نشده است. IMultiServerReportQueryService را تکمیل کنید.");
}
