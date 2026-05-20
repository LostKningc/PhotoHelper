namespace PhotoHelper.Logging;

public sealed class LogMessage
{
    public LogMessage(DateTime timestamp, LogLevel level, string message)
    {
        Timestamp = timestamp;
        Level = level;
        Message = message;
    }

    public DateTime Timestamp { get; }
    public LogLevel Level { get; }
    public string Message { get; }

    public override string ToString() => $"[{Timestamp:HH:mm:ss}] [{Level}] {Message}";
}

public enum LogLevel
{
    Info,
    Warning,
    Error
}
