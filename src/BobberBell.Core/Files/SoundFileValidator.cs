using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BobberBell.Core.Files;

/// <summary>
/// Validates audio file paths before they are handed to a decoder. Paths come from the file picker and from
/// settings.json (which anyone with access to the user profile can edit), so they are treated as untrusted:
/// allow-listed extensions only, a size cap, no device or UNC paths (a UNC path would make Windows send the
/// user's NTLM credentials to a remote host), no alternate data streams, and library sounds must resolve inside
/// the app's Sounds folder (no <c>..</c> traversal).
/// </summary>
public static class SoundFileValidator
{
    /// <summary>Largest sound file accepted, in bytes (50 MB).</summary>
    public const long MaxFileBytes = 50L * 1024 * 1024;

    /// <summary>Longest path accepted.</summary>
    public const int MaxPathLength = 1024;

    /// <summary>Gets the allowed extensions (lower-case, with dot). Matches the file picker filter.</summary>
    public static IReadOnlyCollection<string> AllowedExtensions { get; } = new[] { ".wav", ".mp3", ".flac", ".ogg" };

    /// <summary>Validates a user-chosen or saved sound file.</summary>
    /// <param name="path">Path to check.</param>
    /// <returns>The check result; on success <see cref="SoundFileCheck.FullPath"/> is the canonical path.</returns>
    public static SoundFileCheck Validate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return SoundFileCheck.Fail("empty path");
        if (path.Length > MaxPathLength) return SoundFileCheck.Fail("path too long");
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || path.Any(char.IsControl))
            return SoundFileCheck.Fail("invalid characters");
        if (IsDeviceOrUncPath(path)) return SoundFileCheck.Fail("network and device paths are not allowed");
        if (path.IndexOf(':', 2) >= 0) return SoundFileCheck.Fail("alternate data streams are not allowed");

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return SoundFileCheck.Fail("invalid path");
        }

        if (IsDeviceOrUncPath(full)) return SoundFileCheck.Fail("network and device paths are not allowed");

        string ext = Path.GetExtension(full).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext)) return SoundFileCheck.Fail("unsupported file type");

        var fi = new FileInfo(full);
        if (!fi.Exists) return SoundFileCheck.Fail("file not found", notFound: true);
        if ((fi.Attributes & FileAttributes.Directory) != 0) return SoundFileCheck.Fail("not a file");
        if (fi.Length == 0) return SoundFileCheck.Fail("file is empty");
        if (fi.Length > MaxFileBytes) return SoundFileCheck.Fail("file is larger than 50 MB");
        return SoundFileCheck.Ok(full);
    }

    /// <summary>Validates a bundled library sound and requires it to resolve inside <paramref name="libraryRoot"/>.</summary>
    /// <param name="libraryRoot">The app's Sounds folder.</param>
    /// <param name="fileName">Library file name (no directories).</param>
    /// <returns>The check result.</returns>
    public static SoundFileCheck ValidateLibraryFile(string libraryRoot, string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(libraryRoot);
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            return SoundFileCheck.Fail("invalid library file name");
        string candidate = Path.Combine(libraryRoot, fileName);
        if (!IsUnderDirectory(candidate, libraryRoot)) return SoundFileCheck.Fail("outside the Sounds folder");
        return Validate(candidate);
    }

    /// <summary>Returns whether <paramref name="path"/> resolves inside <paramref name="directory"/> (after <c>..</c> is resolved).</summary>
    /// <param name="path">Candidate path.</param>
    /// <param name="directory">Containing directory.</param>
    /// <returns><c>true</c> if contained.</returns>
    public static bool IsUnderDirectory(string path, string directory)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(path);
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return full.StartsWith(root, cmp);
    }

    private static bool IsDeviceOrUncPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal);
}
