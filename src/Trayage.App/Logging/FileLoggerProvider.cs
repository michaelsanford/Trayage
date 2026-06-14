using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Trayage.App.Logging;

/// <summary>
/// A tiny append-only file logger. A tray WinExe has no console, so this gives us a
/// durable record under %APPDATA%\Trayage\logs for diagnosing field issues.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _filePath;
    private readonly object _gate = new();

    public FileLoggerProvider(string filePath) => _filePath = filePath;

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _filePath, _gate);

    public void Dispose()
    {
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly string _filePath;
        private readonly object _gate;

        public FileLogger(string category, string filePath, object gate)
        {
            _category = category;
            _filePath = filePath;
            _gate = gate;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

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
                .Append(_category).Append(": ")
                .Append(formatter(state, exception));

            if (exception is not null)
            {
                builder.AppendLine().Append(exception);
            }

            var line = builder.AppendLine().ToString();

            lock (_gate)
            {
                try
                {
                    File.AppendAllText(_filePath, line);
                }
                catch (IOException)
                {
                    // Never let logging crash the app.
                }
            }
        }
    }
}
