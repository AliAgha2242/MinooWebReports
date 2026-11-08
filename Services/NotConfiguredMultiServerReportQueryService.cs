using System.Data;
using MinooWebReports.Models;
using RabbitSender;
using RabbitSender.Models.ApiModels;
using RabbitSender.Models.RabbitModel;
using RabbitSender.Utilities;

namespace MinooWebReports.Services;

/// <summary>
/// Sends a parameterized read-only query to each selected server through RabbitSender.
/// The receiver must bind the supplied parameter values as SQL parameters; it must not
/// concatenate them into the query text.
/// </summary>
public sealed class NotConfiguredMultiServerReportQueryService : IMultiServerReportQueryService
{
    public async Task<IReadOnlyList<ServerReportExecutionResult>> ExecuteAsync(
        ReportDefinition report,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyList<VahedTejari> servers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(servers);

        cancellationToken.ThrowIfCancellationRequested();

        if (servers.Count == 0)
            throw new InvalidOperationException("هیچ سروری برای اجرای گزارش انتخاب نشده است.");

        if (!string.Equals(report.SourceType, "Sql", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("اجرای چندسروره فقط برای گزارش‌های SQL پشتیبانی می‌شود.");

        SqlReportExecutionService.ValidateReadOnlySelect(report.SqlQuery ?? string.Empty);
        SqlReportExecutionService.ValidateReportInputs(report, values);

        var parameterValues = report.Parameters.ToDictionary(
            parameter => parameter.Name,
            parameter => values.TryGetValue(parameter.Name, out var value) ? value : null,
            StringComparer.OrdinalIgnoreCase);

        var parameterTypes = report.Parameters.ToDictionary(
            parameter => parameter.Name,
            parameter => parameter.Type,
            StringComparer.OrdinalIgnoreCase);

        // Keep querytext for compatibility with the current Rabbit message contract.
        // Parameter values and types are sent separately so the receiver can bind them
        // safely through SqlParameter instead of replacing text inside the SQL query.
        Dictionary<string, object?> properties = new()
        {
            ["querytext"] = report.SqlQuery,
            ["parameters"] = parameterValues,
            ["parameterTypes"] = parameterTypes
        };

        var client = new Client(
            400.935,
            logProviderEnum: RabbitSender.Models.LogModels.LogProviderEnum.None);

        var sendModels = servers.Select(server => new SendModel
        {
            MessageProcessorName = "Query",
            Properties = properties,
            ReceiverName = server.Server,
            ReceiverVahedeTejariSn = server.VahedetejariSn
        }).ToArray();

        // RabbitSender 6.5.3.4 is currently used through its synchronous API.
        // Run it on a dedicated background thread rather than occupying an ASP.NET
        // request or shared ThreadPool thread. Cancellation cannot interrupt an
        // in-flight synchronous RabbitSender call.
        var sendTask = Task.Factory.StartNew(
            () => client.SendTransactionalParallelism(sendModels),
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        var results = await sendTask.ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (results is null || results.Count != servers.Count)
            throw new InvalidOperationException("پاسخ Rabbit برای تمام سرورهای انتخاب‌شده دریافت نشد.");

        var expectedIds = servers.Select(server => server.VahedetejariSn).ToHashSet();
        var mappedResults = new List<ServerReportExecutionResult>(results.Count);
        var receivedIds = new HashSet<decimal>();

        foreach (var result in results)
        {
            if (result is null)
                throw new InvalidOperationException("یکی از پاسخ‌های Rabbit خالی است.");

            var serverId = result.ReceiverVahedeTejariSN;
            if (!expectedIds.Contains(serverId) || !receivedIds.Add(serverId))
                throw new InvalidOperationException("پاسخ Rabbit شامل سرور نامعتبر یا تکراری است.");

            if (string.IsNullOrWhiteSpace(result.JsonData))
                throw new InvalidOperationException($"پاسخ سرور «{result.ReceiverName}» فاقد داده معتبر است.");

            DataTable? data;
            try
            {
                data = JsonHelper.Deserialize<DataTable>(result.JsonData);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    $"ساختار پاسخ سرور «{result.ReceiverName}» قابل خواندن نیست.", ex);
            }

            if (data is null)
                throw new InvalidOperationException($"پاسخ سرور «{result.ReceiverName}» به DataTable معتبر تبدیل نشد.");

            mappedResults.Add(new ServerReportExecutionResult(
                new VahedTejari
                {
                    Server = result.ReceiverName,
                    VahedetejariDs = result.ReceiverName,
                    VahedetejariSn = serverId
                },
                data));
        }

        if (receivedIds.Count != expectedIds.Count)
            throw new InvalidOperationException("پاسخ Rabbit برای تمام سرورهای انتخاب‌شده کامل نیست.");

        return mappedResults.AsReadOnly();
    }
}
