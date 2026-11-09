using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Minoo.Logging;

public sealed class DailyFileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly object _sync = new();
    private readonly string _directory;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();
    private StreamWriter? _writer;
    private DateOnly? _writerDate;
    private bool _disposed;

    public DailyFileLoggerProvider(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public ILogger CreateLogger(string categoryName) =>
        new DailyFileLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) =>
        _scopeProvider = scopeProvider;

    internal void Write<TState>(
        string category,
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (_disposed || logLevel == LogLevel.None)
            return;

        var now = DateTimeOffset.Now;
        var line = new StringBuilder(512)
            .Append(now.ToString("O", CultureInfo.InvariantCulture))
            .Append(" [").Append(logLevel).Append(']')
            .Append(" [").Append(category).Append(']')
            .Append(" [EventId: ").Append(eventId.Id).Append('] ')
            .Append(FormatState(state, formatter, exception))
            .ToString();

        _scopeProvider.ForEachScope(
            static (scope, builder) => builder.Append(" => Scope: ").Append(scope),
            new StringBuilder());

        var scopeText = new StringBuilder();
        _scopeProvider.ForEachScope(
            static (scope, builder) => builder.Append(" => Scope: ").Append(scope),
            scopeText);

        if (scopeText.Length > 0)
            line += scopeText.ToString();

        if (exception is not null)
            line += Environment.NewLine + exception;

        try
        {
            lock (_sync)
            {
                if (_disposed)
                    return;

                EnsureWriter(now);
                _writer!.WriteLine(line);
                _writer.Flush();
            }
        }
        catch
        {
            // Logging must not break the application if the log disk is unavailable.
        }
    }

    private static string FormatState<TState>(
        TState state,
        Func<TState, Exception?, string> formatter,
        Exception? exception)
    {
        var message = formatter(state, exception);

        if (state is not IEnumerable<KeyValuePair<string, object?>> properties)
            return message;

        var values = properties
            .Where(x => x.Key != "{OriginalFormat}")
            .Select(x => $"{x.Key}={x.Value}");

        var structuredValues = string.Join(", ", values);
        return string.IsNullOrWhiteSpace(structuredValues)
            ? message
            : $"{message} | {structuredValues}";
    }

    private void EnsureWriter(DateTimeOffset now)
    {
        var date = DateOnly.FromDateTime(now.LocalDateTime);
        if (_writer is not null && _writerDate == date)
            return;

        _writer?.Dispose();

        var filePath = Path.Combine(
            _directory,
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");

        var stream = new FileStream(
            filePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite);

        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };

        _writerDate = date;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }

    private sealed class DailyFileLogger(
        DailyFileLoggerProvider provider,
        string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            provider._scopeProvider.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                provider.Write(category, logLevel, eventId, state, exception, formatter);
        }
    }
}
