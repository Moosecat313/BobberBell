namespace BobberBell.Core.Detection;

/// <summary>How a bite is recognized.</summary>
public enum DetectionMode
{
    /// <summary>Match the splash against reference sounds (audio fingerprinting). The default.</summary>
    Fingerprint,

    /// <summary>
    /// A loud, sustained, low-pitched sound after the cast counts as a bite. For servers whose splash sound
    /// differs from the reference recordings, or varies from bite to bite.
    /// </summary>
    VolumeTrigger,
}
