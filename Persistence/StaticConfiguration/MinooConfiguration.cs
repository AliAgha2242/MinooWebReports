using System.Text.Json;

namespace MinooWebReports.Persistence.StaticConfiguration;

public interface IMinooConfiguration
{
    T? Get<T>(string key);
    void Load(string? filePath = null, string fileName = "AppConfig.json");
    List<ServerInfo> GetAllSerevr();
}

public sealed class ServerInfo
{
    public string ServerName { get; init; } = string.Empty;
    public string DatabaseName { get; init; } = string.Empty;
    public decimal VahedeTejariSN { get; init; }
    public string ServerIp { get; init; } = string.Empty;
    public string VahedetejariDs { get; init; } = string.Empty;
}

/// <summary>
/// Reports-side integration of the MinooConfiguration service used by MinooWebCore.
/// Reads ServersInfo from AppConfig.json and reloads it when the file changes.
/// </summary>
public sealed class MinooConfiguration : IMinooConfiguration, IDisposable
{
    private readonly object _sync = new();
    private readonly FileSystemWatcher _watcher;
    private JsonDocument _document;

    public MinooConfiguration(string? filePath = null, string fileName = "AppConfig.json")
    {
        var resolvedPath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName)
            : Path.GetFullPath(filePath);

        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"فایل تنظیمات مینو پیدا نشد: {resolvedPath}", resolvedPath);

        FilePath = resolvedPath;
        _document = JsonDocument.Parse(File.ReadAllText(FilePath));

        _watcher = new FileSystemWatcher(Path.GetDirectoryName(FilePath)!, Path.GetFileName(FilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        _watcher.Changed += (_, _) => ReloadSafely();
        _watcher.Created += (_, _) => ReloadSafely();
        _watcher.Renamed += (_, _) => ReloadSafely();
    }

    private string FilePath { get; }

    public void Load(string? filePath = null, string fileName = "AppConfig.json")
    {
        var path = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName)
            : Path.GetFullPath(filePath);

        if (!File.Exists(path))
            throw new FileNotFoundException($"فایل تنظیمات مینو پیدا نشد: {path}", path);

        var next = JsonDocument.Parse(File.ReadAllText(path));
        lock (_sync)
        {
            _document.Dispose();
            _document = next;
        }
    }

    public T? Get<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_sync)
        {
            JsonElement current = _document.RootElement;
            foreach (var part in key.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                if (current.ValueKind != JsonValueKind.Object ||
                    !current.TryGetProperty(part, out current))
                    throw new KeyNotFoundException($"کلید «{key}» در فایل تنظیمات پیدا نشد.");
            }

            return current.Deserialize<T>();
        }
    }

    public List<ServerInfo> GetAllSerevr()
    {
        lock (_sync)
        {
            if (!_document.RootElement.TryGetProperty("ServersInfo", out var servers) ||
                servers.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("بخش ServersInfo در AppConfig.json وجود ندارد یا معتبر نیست.");

            var result = new List<ServerInfo>();
            foreach (var item in servers.EnumerateArray())
            {
                var server = new ServerInfo
                {
                    ServerName = ReadString(item, "ServerName"),
                    DatabaseName = ReadString(item, "databaseName", "DatabaseName"),
                    VahedeTejariSN = ReadDecimal(item, "VahedeTejariSN"),
                    ServerIp = ReadString(item, "Ip", "ServerIp"),
                    VahedetejariDs = ReadString(item, "VahedetejariDs", "VahedeTejariDs", "BusinessUnitName", "Name")
                };

                if (server.VahedeTejariSN > 0 &&
                    !string.IsNullOrWhiteSpace(server.DatabaseName) &&
                    (!string.IsNullOrWhiteSpace(server.ServerName) || !string.IsNullOrWhiteSpace(server.ServerIp)))
                    result.Add(server);
            }

            if (result.Count == 0)
                throw new InvalidOperationException("هیچ سرور معتبری در بخش ServersInfo پیدا نشد.");

            return result;
        }
    }

    private static string ReadString(JsonElement item, params string[] names)
    {
        foreach (var name in names)
            if (item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString()?.Trim() ?? string.Empty;
        return string.Empty;
    }

    private static decimal ReadDecimal(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        return decimal.TryParse(value.ToString(), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out number) ? number : 0;
    }

    private void ReloadSafely()
    {
        try
        {
            Thread.Sleep(250);
            var next = JsonDocument.Parse(File.ReadAllText(FilePath));
            lock (_sync)
            {
                _document.Dispose();
                _document = next;
            }
        }
        catch (IOException) { }
        catch (JsonException) { }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        lock (_sync) _document.Dispose();
    }
}
