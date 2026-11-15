using System;
using System.Collections.Generic;
using System.IO;

namespace BobberBell.Services;

/// <summary>
/// One-time migration from the pre-rename WeatherBug data folders. Copies exact file names only and never
/// overwrites, then renames each old folder so stale copies don't pile up.
/// </summary>
internal static class LegacyDataMigrator
{
    /// <summary>Runs the one-time WeatherBug → BobberBell data migration.</summary>
    /// <returns>Log lines describing what was migrated or failed (empty when nothing to do).</returns>
    internal static IReadOnlyList<string> Migrate()
    {
        var notes = new List<string>();
        var copied = new List<string>();
        var renamed = new List<string>();
        var failed = new List<string>();
        try
        {
            if (Directory.Exists(AppPaths.LegacyLocalDir))
            {
                Directory.CreateDirectory(AppPaths.AppDataDir);
                foreach (var name in new[] { "settings.json", "log.txt" })
                {
                    string src = Path.Combine(AppPaths.LegacyLocalDir, name), dst = Path.Combine(AppPaths.AppDataDir, name);
                    if (File.Exists(src) && !File.Exists(dst)) { File.Copy(src, dst); copied.Add(name); }
                }
                renamed.Add(RetireLegacyDir(AppPaths.LegacyLocalDir));
            }
        }
        catch (Exception ex) { failed.Add($"settings/log: {ex.Message}"); }
        try
        {
            if (Directory.Exists(AppPaths.LegacyRoamingDir))
            {
                string src = Path.Combine(AppPaths.LegacyRoamingDir, "license.dat");
                if (File.Exists(src) && !File.Exists(AppPaths.LicensePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.LicensePath)!);
                    File.Copy(src, AppPaths.LicensePath);
                    copied.Add("license.dat");
                }
                renamed.Add(RetireLegacyDir(AppPaths.LegacyRoamingDir));
            }
        }
        catch (Exception ex) { failed.Add($"license: {ex.Message}"); }

        if (copied.Count > 0 || renamed.Count > 0)
            notes.Add("WeatherBug data migrated" +
                (copied.Count > 0 ? $" (copied {string.Join(", ", copied)})" : "") +
                (renamed.Count > 0 ? $"; old folders renamed: {string.Join(", ", renamed)}" : ""));
        foreach (var f in failed) notes.Add($"WeatherBug migration failed ({f})");
        return notes;
    }

    // Renames an old data folder to WeatherBug.migrated (or WeatherBug.migrated.2, .3, ...). Returns the new path.
    private static string RetireLegacyDir(string dir)
    {
        string parent = Path.GetDirectoryName(dir)!;
        string target = Path.Combine(parent, "WeatherBug.migrated");
        for (int n = 2; Directory.Exists(target) || File.Exists(target); n++)
            target = Path.Combine(parent, $"WeatherBug.migrated.{n}");
        Directory.Move(dir, target);
        return target;
    }
}
