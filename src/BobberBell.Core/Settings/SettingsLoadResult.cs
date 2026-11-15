namespace BobberBell.Core.Settings;

/// <summary>How a settings load ended.</summary>
public enum SettingsLoadStatus
{
    /// <summary>No settings file; defaults apply.</summary>
    Missing,

    /// <summary>File read, parsed and validated.</summary>
    Loaded,

    /// <summary>File present but unreadable, too large or not valid settings JSON; defaults apply.</summary>
    Failed,
}

/// <summary>Result of <see cref="SettingsStore.Load"/>.</summary>
/// <param name="Status">Outcome.</param>
/// <param name="Validation">Sanitized settings and notes when <see cref="SettingsLoadStatus.Loaded"/>.</param>
/// <param name="Error">Short error message when <see cref="SettingsLoadStatus.Failed"/>.</param>
public sealed record SettingsLoadResult(SettingsLoadStatus Status, SettingsValidationResult? Validation, string? Error);
