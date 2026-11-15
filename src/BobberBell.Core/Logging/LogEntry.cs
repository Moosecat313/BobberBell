using System;

namespace BobberBell.Core.Logging;

/// <summary>A single, already-redacted log line.</summary>
/// <param name="Timestamp">Local time the entry was written.</param>
/// <param name="Level">Severity.</param>
/// <param name="Message">Message text with user-specific paths redacted.</param>
public sealed record LogEntry(DateTime Timestamp, LogLevel Level, string Message);
