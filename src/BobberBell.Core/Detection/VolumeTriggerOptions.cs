namespace BobberBell.Core.Detection;

/// <summary>Settings for <see cref="VolumeTriggerDetector"/>.</summary>
/// <param name="LevelDb">A 20 ms frame counts as loud when its mono peak is at or above this level (dBFS).</param>
/// <param name="MinSeconds">How long a sound must stay loud to count as a bite.</param>
/// <param name="IgnoreSeconds">Sounds in the first seconds of each listen window (the cast) are ignored.</param>
/// <param name="ToneFilter">Only accept sounds whose spectral centroid is between <paramref name="MinToneHz"/> and <paramref name="MaxToneHz"/>.</param>
/// <param name="MinToneHz">Lowest accepted spectral centroid (Hz).</param>
/// <param name="MaxToneHz">Highest accepted spectral centroid (Hz).</param>
/// <param name="BridgeSeconds">Quiet gaps up to this long don't end a sound (a splash flickers around the threshold).</param>
public sealed record VolumeTriggerOptions(
    float LevelDb = VolumeTriggerOptions.DefaultLevelDb,
    double MinSeconds = VolumeTriggerOptions.DefaultMinSeconds,
    double IgnoreSeconds = VolumeTriggerOptions.DefaultIgnoreSeconds,
    bool ToneFilter = true,
    double MinToneHz = VolumeTriggerOptions.DefaultMinToneHz,
    double MaxToneHz = VolumeTriggerOptions.DefaultMaxToneHz,
    double BridgeSeconds = VolumeTriggerOptions.DefaultBridgeSeconds)
{
    /// <summary>Default trigger level (dBFS).</summary>
    public const float DefaultLevelDb = -38f;

    /// <summary>Default minimum sound length (s).</summary>
    public const double DefaultMinSeconds = 0.6;

    /// <summary>Default ignore time at the start of each listen window (s).</summary>
    public const double DefaultIgnoreSeconds = 2.5;

    /// <summary>Default lowest accepted spectral centroid (Hz).</summary>
    public const double DefaultMinToneHz = 300;

    /// <summary>Default highest accepted spectral centroid (Hz).</summary>
    public const double DefaultMaxToneHz = 3200;

    /// <summary>Default gap bridging (s).</summary>
    public const double DefaultBridgeSeconds = 0.1;

    /// <summary>Length of one analysis frame (s).</summary>
    public const double FrameSeconds = 0.02;
}
