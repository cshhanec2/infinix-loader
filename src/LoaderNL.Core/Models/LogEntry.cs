namespace LoaderNL.Core.Models;

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message)
{
    public string DisplayTime => Timestamp.ToLocalTime().ToString("HH:mm:ss");
}
