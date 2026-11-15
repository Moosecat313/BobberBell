using System;
using System.Collections.Generic;
using System.Linq;

namespace BobberBell.Core.Logging;

/// <summary>
/// Replaces user-specific folder roots in text with neutral labels
/// (<c>%LOCALAPPDATA%</c>, <c>%APPDATA%</c>, <c>~</c>) so logs never reveal the Windows user name.
/// </summary>
public sealed class PathRedactor
{
    private readonly (string Root, string Label)[] roots;
    private readonly string? userName;

    /// <summary>Creates a redactor for explicit roots (longest roots are replaced first).</summary>
    /// <param name="roots">Folder roots and the label that replaces each.</param>
    /// <param name="userName">Windows user name; <c>\Users\name</c> fragments are replaced when it has 3+ characters.</param>
    public PathRedactor(IEnumerable<(string Root, string Label)> roots, string? userName)
    {
        ArgumentNullException.ThrowIfNull(roots);
        this.roots = roots
            .Where(r => !string.IsNullOrEmpty(r.Root))
            .Select(r => (r.Root.TrimEnd('\\'), r.Label))
            .OrderByDescending(r => r.Item1.Length)
            .ToArray();
        this.userName = userName;
    }

    /// <summary>A redactor for the current user's LocalAppData, AppData and profile folders.</summary>
    /// <returns>A redactor configured from <see cref="Environment"/>.</returns>
    public static PathRedactor ForCurrentUser() => new(
        new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~"),
        },
        Environment.UserName);

    /// <summary>Returns <paramref name="text"/> with known roots replaced (case-insensitive).</summary>
    /// <param name="text">Text that may contain paths.</param>
    /// <returns>The redacted text (empty for null).</returns>
    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        foreach (var (root, label) in roots)
            text = text.Replace(root, label, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(userName) && userName.Length >= 3)
            text = text.Replace(@"\Users\" + userName, @"\Users\~", StringComparison.OrdinalIgnoreCase);
        return text;
    }
}
