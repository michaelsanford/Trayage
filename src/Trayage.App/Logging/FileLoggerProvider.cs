using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Trayage.App.Logging;

/// <summary>
/// A tiny append-only file logger. A tray WinExe has no console, so this gives us a
/// durable record under %APPDATA%\Trayage\logs for diagnosing field issues.
/// </summary>
/// <remarks>
/// One stream stays open for the provider's lifetime instead of reopening the file for every
/// line, and every line is flushed as it's written, so the log is complete even when the
/// process is terminated at session end (see <c>App.OnSessionEnding</c>). The file is shared
/// for read, write and delete, so it can be tailed or deleted while the app runs.
/// </remarks>
public sealed class FileLoggerProvider(string filePath, LogLevel minLevel = LogLevel.Information) : ILoggerProvider
{
    private const long MaxBytes = 10 * 1024 * 1024;

    private readonly Lock _gate = new();
    private StreamWriter? _writer;
    private long _length;
    private bool _disposed;

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName, minLevel);

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            CloseWriter();
        }
    }

    private void Write(string line)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                if (_writer is not null && _length > MaxBytes)
                {
                    Rotate();
                }

                _writer ??= OpenWriter();
                _writer.Write(line);
                _length += Encoding.UTF8.GetByteCount(line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                // Never let logging crash the app; drop the writer so the next line retries.
                CloseWriter();
            }
        }
    }

    private StreamWriter OpenWriter()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        if (File.Exists(filePath) && new FileInfo(filePath).Length > MaxBytes)
        {
            Rotate();
        }

        var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

        // Still oversized means rotation failed: count from zero so it's retried after another
        // MaxBytes of logging, not on every line.
        _length = stream.Length > MaxBytes ? 0 : stream.Length;

        return new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
    }

    /// <summary>Keeps one previous generation (<c>trayage.log.1</c>) and starts a fresh file.</summary>
    private void Rotate()
    {
        CloseWriter();
        try
        {
            File.Move(filePath, filePath + ".1", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Rotation failed (e.g. the file is open elsewhere without delete sharing):
            // keep appending to the current file rather than losing lines.
        }
    }

    private void CloseWriter()
    {
        try
        {
            _writer?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Nothing useful to do; the next write reopens.
        }

        _writer = null;
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category, LogLevel minLevel) : ILogger
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

            provider.Write(builder.AppendLine().ToString());
        }
    }
}
