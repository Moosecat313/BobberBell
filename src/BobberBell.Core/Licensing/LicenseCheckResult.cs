namespace BobberBell.Core.Licensing;

/// <summary>Outcome of checking a license key the user typed.</summary>
public enum LicenseCheckResult
{
    /// <summary>Key accepted and saved, bound to this PC.</summary>
    Activated,

    /// <summary>Key accepted, but not saved because the machine id could not be read.</summary>
    SessionOnly,

    /// <summary>Key has letters/digits but fewer than <see cref="LicenseService.MinCodeLength"/>; never hashed.</summary>
    TooShort,

    /// <summary>Key is empty or unknown.</summary>
    Invalid,
}
