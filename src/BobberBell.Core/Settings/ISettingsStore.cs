namespace BobberBell.Core.Settings;

/// <summary>Loads and saves <see cref="AppSettings"/>.</summary>
public interface ISettingsStore
{
    /// <summary>Gets the full path of the settings file.</summary>
    string SettingsPath { get; }

    /// <summary>Reads, size-checks, parses and validates the settings file.</summary>
    /// <returns>The outcome; never throws for bad files.</returns>
    SettingsLoadResult Load();

    /// <summary>Atomically writes the settings file (temp file, then replace).</summary>
    /// <param name="settings">Settings to save.</param>
    void Save(AppSettings settings);
}
