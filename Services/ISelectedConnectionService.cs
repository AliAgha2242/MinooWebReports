using MinooWebReports.Models;

namespace MinooWebReports.Services;

public interface ISelectedConnectionService
{
    VahedTejari? Current { get; }
    Task SelectAsync(VahedTejari connection, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
