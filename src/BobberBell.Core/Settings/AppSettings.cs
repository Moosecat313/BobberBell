namespace BobberBell.Core.Settings;

/// <summary>
/// Persisted user settings (<c>%LOCALAPPDATA%\BobberBell\settings.json</c>).
/// Property names are the on-disk JSON schema; do not rename them.
/// "Send inputs" is intentionally not persisted: it always starts off.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Gets or sets the fishing key name.</summary>
    public string SelectedKey { get; set; } = Input.KeyCatalog.DefaultFishingKey;

    /// <summary>Gets or sets the minimum fingerprint similarity (0.15 to 0.95).</summary>
    public decimal Similarity { get; set; } = SettingsLimits.DefaultSimilarity;

    /// <summary>Gets or sets the minimum volume in dBFS (-80 to 0).</summary>
    public decimal MinVolume { get; set; } = SettingsLimits.DefaultMinVolume;

    /// <summary>Gets or sets the window title.</summary>
    public string WindowName { get; set; } = SettingsLimits.DefaultWindowName;

    /// <summary>Gets or sets the last custom sound file, or empty.</summary>
    public string LastSoundPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the library entry ("BobberSplash" or "Custom file…").</summary>
    public string LibrarySound { get; set; } = SettingsLimits.LibraryAllTargets;

    /// <summary>Gets or sets the audio output device friendly name, or empty for default.</summary>
    public string AudioDevice { get; set; } = string.Empty;

    /// <summary>Gets or sets the minimum cast wait in seconds.</summary>
    public decimal CastWaitMin { get; set; } = 1.5m;

    /// <summary>Gets or sets the maximum cast wait in seconds.</summary>
    public decimal CastWaitMax { get; set; } = 2.5m;

    /// <summary>Gets or sets a value indicating whether the session auto-stops (on by default).</summary>
    public bool AutoStopEnabled { get; set; } = true;

    /// <summary>Gets or sets the session length in hours (0 = no limit, max 168).</summary>
    public decimal AutoStopHours { get; set; } = SettingsLimits.DefaultSessionHours;

    /// <summary>Gets or sets a value indicating whether timed buffs are on.</summary>
    public bool BuffEnabled { get; set; }

    /// <summary>Gets or sets the buff interval in whole minutes.</summary>
    public decimal BuffIntervalMinutes { get; set; } = 10m;

    /// <summary>Gets or sets the buff key name.</summary>
    public string BuffKey { get; set; } = Input.KeyCatalog.DefaultBuffKey;

    /// <summary>Gets or sets the rod key name.</summary>
    public string RodKey { get; set; } = Input.KeyCatalog.DefaultRodKey;

    /// <summary>Gets or sets the wait after each buff key press, in seconds.</summary>
    public decimal BuffWaitSeconds { get; set; } = 3m;

    /// <summary>Gets or sets a value indicating whether a buff is applied before the first cast.</summary>
    public bool BuffOnStart { get; set; }

    /// <summary>Gets or sets the detection mode: "VolumeTrigger" (default) or "Fingerprint".</summary>
    public string DetectionMode { get; set; } = SettingsLimits.DefaultDetectionMode;

    /// <summary>Gets or sets the volume-trigger level in dBFS (-80 to 0).</summary>
    public decimal TriggerLevelDb { get; set; } = SettingsLimits.DefaultTriggerLevelDb;

    /// <summary>Gets or sets how long a sound must stay loud to count as a bite, in seconds.</summary>
    public decimal TriggerMinSeconds { get; set; } = SettingsLimits.DefaultTriggerMinSeconds;

    /// <summary>Gets or sets the time ignored at the start of each listen window (the cast sound), in seconds.</summary>
    public decimal TriggerIgnoreSeconds { get; set; } = SettingsLimits.DefaultTriggerIgnoreSeconds;

    /// <summary>Gets or sets a value indicating whether the splash tone filter is on (default on).</summary>
    public bool TriggerToneFilter { get; set; } = true;

    /// <summary>Gets or sets the lowest accepted spectral centroid in Hz.</summary>
    public decimal TriggerMinToneHz { get; set; } = SettingsLimits.DefaultTriggerMinToneHz;

    /// <summary>Gets or sets the highest accepted spectral centroid in Hz.</summary>
    public decimal TriggerMaxToneHz { get; set; } = SettingsLimits.DefaultTriggerMaxToneHz;
}
