using System;
using System.Globalization;
using System.IO;

namespace BobberBell.Core.Logging;

/// <summary>
/// <see cref="IAppLogger"/> that appends redacted lines to a log file (rotated at about 2 MB to
/// <c>log.old.txt</c>) and raises <see cref="EntryWritten"/> for the UI. File errors never throw.
/// </summary>
public sealed class FileAppLogger : IAppLogger
{
    /// <summary>Size in bytes above which the log file is rotated.</summary>
    public const long RotateBytes = 2_000_000;

    private readonly PathRedactor redactor;
    private readonly Func<DateTime> clock;
    private readonly object gate = new();

    /// <summary>Creates a logger writing to <paramref name="logPath"/>.</summary>
    /// <param name="logPath">Full path of the log file.</param>
    /// <param name="redactor">Redactor applied to every message.</param>
    /// <param name="clock">Optional clock (tests); defaults to <see cref="DateTime.Now"/>.</param>
    public FileAppLogger(string logPath, PathRedactor redactor, Func<DateTime>? clock = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(logPath);
        LogPath = logPath;
        this.redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        this.clock = clock ?? (() => DateTime.Now);
    }

    /// <inheritdoc />
    public event EventHandler<LogEntry>? EntryWritten;

    /// <summary>Gets the full path of the current log file.</summary>
    public string LogPath { get; }

    /// <inheritdoc />
    public void Info(string message) => Write(LogLevel.Info, message);

    /// <inheritdoc />
    public void Warn(string message) => Write(LogLevel.Warning, message);

    /// <inheritdoc />
    public void Error(string message, Exception? exception = null) =>
        Write(LogLevel.Error, exception == null ? message : $"{message}: {exception.Message}");

    private void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(clock(), level, redactor.Redact(message));
        AppendToFile(entry);
        EntryWritten?.Invoke(this, entry);
    }

    private void AppendToFile(LogEntry entry)
    {
        try
        {
            lock (gate)
            {
                string dir = Path.GetDirectoryName(LogPath) ?? ".";
                Directory.CreateDirectory(dir);
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > RotateBytes)
                    File.Move(LogPath, Path.Combine(dir, "log.old.txt"), true);
                File.AppendAllText(
                    LogPath,
                    entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + entry.Message + Environment.NewLine);
            }
        }
        catch (IOException)
        {
            // Logging must never take the app down.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: a read-only or locked log folder is not fatal.
        }
    }
}
