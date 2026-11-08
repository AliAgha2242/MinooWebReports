using MinooWebReports.Models;
using RabbitSender;



using RabbitSender.Models.ApiModels;
using RabbitSender.Models.RabbitModel;
using RabbitSender.Utilities;
using System.Data;
using System.Linq;
namespace MinooWebReports.Services;

/// <summary>
/// Temporary implementation to keep the application resolvable until the real
/// multi-server query service is supplied.
/// Replace this registration in Program.cs when implementing the service.
/// </summary>
public sealed class NotConfiguredMultiServerReportQueryService : IMultiServerReportQueryService
{
    public async Task<IReadOnlyList<ServerReportExecutionResult>> ExecuteAsync(
        ReportDefinition report,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyList<VahedTejari> servers,
        CancellationToken cancellationToken = default)
    {
        foreach (var item in report.Parameters)
        {
            report.SqlQuery.ToLower().Replace(item.Name.ToLower(), item.ValueField??item.DefaultValue);
        }
       
        Dictionary<string, object?>? keyValuePairs = new Dictionary<string, object?>()
        {
            { "querytext",report.SqlQuery},
        };
        

        Client client = new Client(400.935, logProviderEnum: RabbitSender.Models.LogModels.LogProviderEnum.None);
        IList<BaseOperationResult> results = client.SendTransactionalParallelism(servers.Select(p => new SendModel() { MessageProcessorName = "Query", Properties = keyValuePairs, ReceiverName = p.Server, ReceiverVahedeTejariSn = p.VahedetejariSn }).ToArray());
        IReadOnlyList<ServerReportExecutionResult> res = results.Select(p => new ServerReportExecutionResult(new VahedTejari()
        {
            Server = p.ReceiverName,
            VahedetejariDs = p.ReceiverName,
            VahedetejariSn = p.ReceiverVahedeTejariSN
        }, JsonHelper.Deserialize<DataTable>(p.JsonData))).ToList().AsReadOnly();
        await Task.CompletedTask;
        return res;

    }
}
