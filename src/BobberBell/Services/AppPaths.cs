using System;
using System.IO;

namespace BobberBell.Services;

/// <summary>Every file and folder location the app uses. Per-user data never lives next to the exe.</summary>
internal static class AppPaths
{
    /// <summary>Gets <c>%LOCALAPPDATA%\BobberBell</c> (settings and logs).</summary>
    internal static string AppDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BobberBell");

    /// <summary>Gets the settings file path.</summary>
    internal static string SettingsPath { get; } = Path.Combine(AppDataDir, "settings.json");

    /// <summary>Gets the pre-1.0 settings file location next to the exe (migrated once).</summary>
    internal static string LegacySettingsPath { get; } = Path.Combine(AppContext.BaseDirectory, "settings.json");

    /// <summary>Gets the log file path.</summary>
    internal static string LogPath { get; } = Path.Combine(AppDataDir, "log.txt");

    /// <summary>Gets <c>%LOCALAPPDATA%\BobberBell\trial.dat</c> (signed used-trial seconds; mirrored in HKCU\Software\BobberBell).</summary>
    internal static string TrialPath { get; } = Path.Combine(AppDataDir, "trial.dat");

    /// <summary>Gets <c>%APPDATA%\BobberBell\license.dat</c>.</summary>
    internal static string LicensePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BobberBell", "license.dat");

    /// <summary>Gets the pre-rename local data folder (WeatherBug).</summary>
    internal static string LegacyLocalDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeatherBug");

    /// <summary>Gets the pre-rename roaming data folder (WeatherBug).</summary>
    internal static string LegacyRoamingDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WeatherBug");

    /// <summary>Gets the bundled sound library folder.</summary>
    internal static string SoundsDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Sounds");

    /// <summary>Gets the bundled image folder.</summary>
    internal static string AssetsDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Assets");
}
