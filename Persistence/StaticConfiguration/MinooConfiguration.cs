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
    private FileSystemWatcher? _watcher;
    private JsonDocument _document = null!;
    private string _filePath = string.Empty;

    public MinooConfiguration(string? filePath = null, string fileName = "AppConfig.json")
    {
        Load(filePath, fileName);
    }

    private string FilePath
    {
        get { lock (_sync) return _filePath; }
    }

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
            _document?.Dispose();
            _document = next;
            _filePath = path;

            _watcher?.Dispose();
            var watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            watcher.Changed += (_, _) => ReloadSafely();
            watcher.Created += (_, _) => ReloadSafely();
            watcher.Renamed += (_, _) => ReloadSafely();
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
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
                    !TryGetPropertyIgnoreCase(current, part, out current))
                    throw new KeyNotFoundException($"کلید «{key}» در فایل تنظیمات پیدا نشد.");
            }

            return current.Deserialize<T>();
        }
    }

    public List<ServerInfo> GetAllSerevr()
    {
        lock (_sync)
        {
            if (!TryGetPropertyIgnoreCase(_document.RootElement, "ServersInfo", out var servers) ||
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
                    VahedetejariDs = ReadString(item, "VahedetejariDs", "VahedeTejariDs", "VahedeTejariDS", "VahedetejariDS", "VahedeTejariName", "BusinessUnitName", "UnitName", "Name")
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

    private static bool TryGetPropertyIgnoreCase(JsonElement item, string name, out JsonElement value)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in item.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string ReadString(JsonElement item, params string[] names)
    {
        foreach (var name in names)
            if (TryGetPropertyIgnoreCase(item, name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString()?.Trim() ?? string.Empty;
        return string.Empty;
    }

    private static decimal ReadDecimal(JsonElement item, string name)
    {
        if (!TryGetPropertyIgnoreCase(item, name, out var value)) return 0;
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
        lock (_sync)
        {
            _watcher?.Dispose();
            _document.Dispose();
        }
    }
}
