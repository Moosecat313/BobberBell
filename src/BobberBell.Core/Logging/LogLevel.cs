namespace BobberBell.Core.Logging;

/// <summary>Severity of a log entry.</summary>
public enum LogLevel
{
    /// <summary>Normal activity (casts, bites, settings saved).</summary>
    Info,

    /// <summary>Something unexpected that the app recovered from.</summary>
    Warning,

    /// <summary>An operation failed.</summary>
    Error,
}
