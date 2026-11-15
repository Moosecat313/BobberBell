namespace BobberBell.Core.Settings;

/// <summary>Allowed ranges and defaults for every setting. The UI controls use the same values.</summary>
public static class SettingsLimits
{
    /// <summary>Display version used in titles.</summary>
    public const string AppVersion = "1.0.6";

    /// <summary>Default window title.</summary>
    public const string DefaultWindowName = "BobberBell - v" + AppVersion;

    /// <summary>Library entry that loads all three BobberSplash sounds.</summary>
    public const string LibraryAllTargets = "BobberSplash";

    /// <summary>Library entry for a user-chosen file.</summary>
    public const string LibraryCustomFile = "Custom file…";

    /// <summary>Lowest similarity threshold.</summary>
    public const decimal MinSimilarity = 0.15m;

    /// <summary>Highest similarity threshold.</summary>
    public const decimal MaxSimilarity = 0.95m;

    /// <summary>Default similarity threshold.</summary>
    public const decimal DefaultSimilarity = 0.40m;

    /// <summary>Lowest volume threshold (dBFS).</summary>
    public const decimal MinVolumeDb = -80m;

    /// <summary>Highest volume threshold (dBFS).</summary>
    public const decimal MaxVolumeDb = 0m;

    /// <summary>Default volume threshold (dBFS).</summary>
    public const decimal DefaultMinVolume = -35m;

    /// <summary>Shortest cast wait (s).</summary>
    public const decimal MinCastWait = 0.5m;

    /// <summary>Longest cast wait (s).</summary>
    public const decimal MaxCastWait = 10m;

    /// <summary>Longest session (h); larger values are capped.</summary>
    public const decimal MaxSessionHours = 168m;

    /// <summary>Default session length (h); also used for invalid negative values.</summary>
    public const decimal DefaultSessionHours = 0.25m;

    /// <summary>Shortest buff interval (min).</summary>
    public const decimal MinBuffInterval = 1m;

    /// <summary>Longest buff interval (min).</summary>
    public const decimal MaxBuffInterval = 240m;

    /// <summary>Shortest buff wait (s).</summary>
    public const decimal MinBuffWait = 0.5m;

    /// <summary>Longest buff wait (s).</summary>
    public const decimal MaxBuffWait = 30m;

    /// <summary>Longest window title kept.</summary>
    public const int MaxWindowNameLength = 128;

    /// <summary>Longest audio device name kept.</summary>
    public const int MaxDeviceNameLength = 256;

    /// <summary>Largest settings file accepted, in bytes.</summary>
    public const int MaxSettingsFileBytes = 64 * 1024;

    /// <summary>Detection mode: fingerprint matching.</summary>
    public const string DetectionModeFingerprint = "Fingerprint";

    /// <summary>Detection mode: volume trigger (default).</summary>
    public const string DetectionModeVolumeTrigger = "VolumeTrigger";

    /// <summary>Default detection mode.</summary>
    public const string DefaultDetectionMode = DetectionModeVolumeTrigger;

    /// <summary>Default volume-trigger level (dBFS).</summary>
    public const decimal DefaultTriggerLevelDb = -38m;

    /// <summary>Shortest volume-trigger sound length (s).</summary>
    public const decimal MinTriggerSeconds = 0.1m;

    /// <summary>Longest volume-trigger sound length (s).</summary>
    public const decimal MaxTriggerSeconds = 3m;

    /// <summary>Default volume-trigger sound length (s).</summary>
    public const decimal DefaultTriggerMinSeconds = 0.6m;

    /// <summary>Longest ignore time after a cast (s).</summary>
    public const decimal MaxTriggerIgnoreSeconds = 10m;

    /// <summary>Default ignore time after a cast (s).</summary>
    public const decimal DefaultTriggerIgnoreSeconds = 2.5m;

    /// <summary>Lowest tone-filter bound (Hz).</summary>
    public const decimal MinToneHz = 50m;

    /// <summary>Highest tone-filter bound (Hz).</summary>
    public const decimal MaxToneHz = 20000m;

    /// <summary>Default lowest accepted spectral centroid (Hz).</summary>
    public const decimal DefaultTriggerMinToneHz = 300m;

    /// <summary>Default highest accepted spectral centroid (Hz).</summary>
    public const decimal DefaultTriggerMaxToneHz = 3200m;
}
