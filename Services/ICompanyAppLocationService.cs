using MinooWebReports.Models;

namespace MinooWebReports.Services;

public interface ICompanyAppLocationService
{
    Task<IReadOnlyList<VahedTejari>> GetAllLocationAsync(
        UserModel user,
        CancellationToken cancellationToken = default);
}
