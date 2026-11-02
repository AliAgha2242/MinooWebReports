using MinooWebReports.Models;
using MinooWebReports.Persistence.StaticConfiguration;

namespace MinooWebReports.Services;

public sealed class ServerDirectoryService(IMinooConfiguration configuration) : IServerDirectoryService
{
    public IReadOnlyList<VahedTejari> GetAll() =>
        configuration.GetAllSerevr()
            .Select(Map)
            .OrderBy(item => item.VahedetejariDs, StringComparer.CurrentCulture)
            .ToArray();

    public VahedTejari? GetBySn(decimal vahedetejariSn) =>
        GetAll().SingleOrDefault(item => item.VahedetejariSn == vahedetejariSn);

    private static VahedTejari Map(ServerInfo server) => new()
    {
        VahedetejariSn = server.VahedeTejariSN,
        VahedetejariDs = string.IsNullOrWhiteSpace(server.VahedetejariDs)
            ? $"واحد تجاری {server.VahedeTejariSN:0.##}"
            : server.VahedetejariDs,
        Server = string.IsNullOrWhiteSpace(server.ServerName) ? server.ServerIp : server.ServerName,
        ServerIp = server.ServerIp,
        DataBAset = server.DatabaseName
    };
}
