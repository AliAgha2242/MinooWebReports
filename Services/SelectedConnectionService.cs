using System.Text.Json;
using MinooWebReports.Models;

namespace MinooWebReports.Services;

public sealed class SelectedConnectionService(IHttpContextAccessor httpContextAccessor)
    : ISelectedConnectionService
{
    private const string SessionKey = "SelectedVahedTejari";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private ISession Session => httpContextAccessor.HttpContext?.Session
        ?? throw new InvalidOperationException("An active HTTP session is required.");

    public VahedTejari? Current
    {
        get
        {
            var json = Session.GetString(SessionKey);
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<VahedTejari>(json, JsonOptions);
        }
    }

    public Task SelectAsync(VahedTejari connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (connection.VahedetejariSn <= 0 ||
            string.IsNullOrWhiteSpace(connection.Server) ||
            string.IsNullOrWhiteSpace(connection.DataBAset))
        {
            throw new ArgumentException("اطلاعات محل اتصال معتبر نیست.", nameof(connection));
        }

        Session.SetString(SessionKey, JsonSerializer.Serialize(connection, JsonOptions));
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Session.Remove(SessionKey);
        return Task.CompletedTask;
    }
}
