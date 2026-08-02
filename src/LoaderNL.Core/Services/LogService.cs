using LoaderNL.Core.Models;

namespace LoaderNL.Core.Services;

public sealed class LogService
{
    private readonly object _sync = new();
    private readonly List<LogEntry> _entries = [];

    public LogService(string? logDirectory = null)
    {
        LogDirectory = logDirectory ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LoaderNL",
                "logs");
        LogFilePath = Path.Combine(LogDirectory, "launcher.log");
    }

    public event EventHandler<LogEntry>? EntryWritten;

    public string LogDirectory { get; }
    public string LogFilePath { get; }

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_sync)
            {
                return _entries.ToArray();
            }
        }
    }

    public void Info(string message) => Write(LogLevel.Info, message);
    public void Success(string message) => Write(LogLevel.Success, message);
    public void Warning(string message) => Write(LogLevel.Warning, message);
    public void Error(string message) => Write(LogLevel.Error, message);

    private void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message);

        lock (_sync)
        {
            _entries.Add(entry);
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(
                    LogFilePath,
                    $"{entry.Timestamp:O}\t{entry.Level}\t{entry.Message}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // Logging must never prevent the launcher from starting.
            }
            catch (UnauthorizedAccessException)
            {
                // Keep the in-memory log when the configured directory is unavailable.
            }
        }

        EntryWritten?.Invoke(this, entry);
    }
}
