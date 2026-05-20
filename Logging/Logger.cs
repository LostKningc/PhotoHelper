using System.Collections.Concurrent;
using System.IO;

namespace PhotoHelper.Logging;

public sealed class Logger
{
    private readonly object _fileLock = new();
    private readonly string _logFilePath;
    private readonly ConcurrentQueue<LogMessage> _pendingMessages = new();

    public Logger(string logDirectory)
    {
        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            throw new ArgumentException("Log directory must be provided.", nameof(logDirectory));
        }

        Directory.CreateDirectory(logDirectory);
        _logFilePath = Path.Combine(logDirectory, $"photohelper_{DateTime.Now:yyyyMMdd}.log");
    }

    public event EventHandler<LogMessage>? LogEmitted;

    public void Info(string message) => Write(LogLevel.Info, message);

    public void Warning(string message) => Write(LogLevel.Warning, message);

    public void Error(string message) => Write(LogLevel.Error, message);

    private void Write(LogLevel level, string message)
    {
        var logMessage = new LogMessage(DateTime.Now, level, message);
        _pendingMessages.Enqueue(logMessage);
        FlushPendingMessages();
        LogEmitted?.Invoke(this, logMessage);
    }

    private void FlushPendingMessages()
    {
        if (_pendingMessages.IsEmpty)
        {
            return;
        }

        lock (_fileLock)
        {
            using var stream = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);

            while (_pendingMessages.TryDequeue(out var message))
            {
                writer.WriteLine(message.ToString());
            }
        }
    }
}
