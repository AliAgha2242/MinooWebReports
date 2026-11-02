using MinooWebReports.Models;

namespace MinooWebReports.Services;

public interface IServerDirectoryService
{
    IReadOnlyList<VahedTejari> GetAll();
    VahedTejari? GetBySn(decimal vahedetejariSn);
}
