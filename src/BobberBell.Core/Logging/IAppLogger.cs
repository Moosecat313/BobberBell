using System;

namespace BobberBell.Core.Logging;

/// <summary>
/// The single application logger. Every message is path-redacted before it is stored or shown,
/// so log files and the Log tab never contain the Windows user name.
/// </summary>
public interface IAppLogger
{
    /// <summary>Raised after an entry has been written (on the calling thread).</summary>
    event EventHandler<LogEntry>? EntryWritten;

    /// <summary>Writes an informational message.</summary>
    /// <param name="message">Message text; paths are redacted.</param>
    void Info(string message);

    /// <summary>Writes a warning.</summary>
    /// <param name="message">Message text; paths are redacted.</param>
    void Warn(string message);

    /// <summary>Writes an error, optionally with the exception that caused it (only its message is logged).</summary>
    /// <param name="message">Message text; paths are redacted.</param>
    /// <param name="exception">Optional cause; only <see cref="Exception.Message"/> is written.</param>
    void Error(string message, Exception? exception = null);
}
