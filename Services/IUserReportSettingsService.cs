using MinooWebReports.Models;

namespace MinooWebReports.Services;

public interface IUserReportSettingsService
{
    Task<GridPreferences> GetAsync(int reportId, CancellationToken cancellationToken = default);
    Task SaveAsync(int reportId, GridPreferences preferences, CancellationToken cancellationToken = default);
}
