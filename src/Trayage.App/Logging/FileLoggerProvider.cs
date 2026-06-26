using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Trayage.App.Logging;

/// <summary>
/// A tiny append-only file logger. A tray WinExe has no console, so this gives us a
/// durable record under %APPDATA%\Trayage\logs for diagnosing field issues.
/// </summary>
public sealed partial class FileLoggerProvider(string filePath, LogLevel minLevel = LogLevel.Information) : ILoggerProvider
{
    private readonly object _gate = new();

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, filePath, minLevel, _gate);

    public void Dispose()
    {
    }

    private sealed class FileLogger(string category, string filePath, LogLevel minLevel, object gate) : ILogger
    {

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var builder = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" [").Append(logLevel).Append("] ")
                .Append(category).Append(": ")
                .Append(formatter(state, exception));

            if (exception is not null)
            {
                builder.AppendLine().Append(exception);
            }

            var line = builder.AppendLine().ToString();

            lock (gate)
            {
                try
                {
                    if (File.Exists(filePath) && new FileInfo(filePath).Length > 10 * 1024 * 1024)
                    {
                        var backupPath = filePath + ".1";
                        try
                        {
                            if (File.Exists(backupPath))
                            {
                                File.Delete(backupPath);
                            }
                            File.Move(filePath, backupPath);
                        }
                        catch (IOException)
                        {
                            // If rotation fails (e.g. file is locked), just write to the original file
                        }
                    }

                    File.AppendAllText(filePath, line);
                }
                catch (IOException)
                {
                    // Never let logging crash the app.
                }
            }
        }
    }
}
