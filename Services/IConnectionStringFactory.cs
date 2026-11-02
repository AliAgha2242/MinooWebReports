using MinooWebReports.Models;

namespace MinooWebReports.Services;

public interface IConnectionStringFactory
{
    string Create(VahedTejari connection);
}
