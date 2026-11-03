using System.Security.Claims;
using System.Text.Json;
using MinooWebReports.Models;
using StackExchange.Redis;

namespace MinooWebReports.Services;

public sealed class UserReportSettingsService(
    IConnectionMultiplexer redis,
    IHttpContextAccessor httpContextAccessor) : IUserReportSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDatabase _database = redis.GetDatabase();

    public async Task<GridPreferences> GetAsync(
        int reportId,
        CancellationToken cancellationToken = default)
    {
        ValidateReportId(reportId);

        var value = await _database.StringGetAsync(CreateKey(reportId));
        if (value.IsNullOrEmpty)
            return new GridPreferences();

        try
        {
            return JsonSerializer.Deserialize<GridPreferences>((string)value!, JsonOptions)
                ?? new GridPreferences();
        }
        catch (JsonException)
        {
            return new GridPreferences();
        }
    }

    public async Task SaveAsync(
        int reportId,
        GridPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ValidateReportId(reportId);
        ArgumentNullException.ThrowIfNull(preferences);

        preferences.PageSize = Math.Clamp(preferences.PageSize, 10, 200);
        preferences.HiddenColumns = preferences.HiddenColumns
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToList();

        preferences.GroupColumns = preferences.GroupColumns
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        preferences.ColumnWidths = preferences.ColumnWidths
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .Take(200)
            .ToDictionary(x => x.Key, x => Math.Clamp(x.Value, 90, 800), StringComparer.OrdinalIgnoreCase);

        preferences.Theme = preferences.Theme is "midnight" or "light" or "ocean" or "contrast"
            ? preferences.Theme
            : "midnight";

        var json = JsonSerializer.Serialize(preferences, JsonOptions);
        await _database.StringSetAsync(CreateKey(reportId), json);
    }

    private RedisKey CreateKey(int reportId)
    {
        var context = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("An active HTTP context is required.");

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");

        // Until authentication is configured, isolate anonymous visitors by session.
        userId ??= $"session-{context.Session.Id}";

        return $"MinooWebReports:UserReport:{userId}:{reportId}";
    }

    private static void ValidateReportId(int reportId)
    {
        if (reportId <= 0)
            throw new ArgumentOutOfRangeException(nameof(reportId));
    }
}
