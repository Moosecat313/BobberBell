namespace BobberBell.Core.Files;

/// <summary>Result of validating a sound file path.</summary>
/// <param name="IsValid">Whether the file may be opened.</param>
/// <param name="FullPath">Canonical full path when valid; otherwise empty.</param>
/// <param name="Reason">Short reason when invalid (safe to log; contains no file contents).</param>
/// <param name="NotFound">True when the only problem is that the file does not exist.</param>
public readonly record struct SoundFileCheck(bool IsValid, string FullPath, string Reason, bool NotFound = false)
{
    /// <summary>Creates a successful result.</summary>
    /// <param name="fullPath">Canonical path.</param>
    /// <returns>A valid result.</returns>
    public static SoundFileCheck Ok(string fullPath) => new(true, fullPath, string.Empty);

    /// <summary>Creates a failed result.</summary>
    /// <param name="reason">Why the path was rejected.</param>
    /// <param name="notFound">Whether the file is simply missing.</param>
    /// <returns>An invalid result.</returns>
    public static SoundFileCheck Fail(string reason, bool notFound = false) => new(false, string.Empty, reason, notFound);
}
