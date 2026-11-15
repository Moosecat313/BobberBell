using System;
using System.IO;
using System.Text.Json;
using BobberBell.Core.Files;

namespace BobberBell.Core.Settings;

/// <summary>
/// JSON settings file with defensive parsing: a size cap before reading, a strict source-generated
/// contract (no reflection-based or polymorphic deserialization), then range validation of every value.
/// </summary>
public sealed class SettingsStore : ISettingsStore
{
    private readonly Func<string, SoundFileCheck>? soundCheck;

    /// <summary>Creates a store for <paramref name="settingsPath"/>.</summary>
    /// <param name="settingsPath">Full path of settings.json.</param>
    /// <param name="soundCheck">Optional sound path validator (tests).</param>
    public SettingsStore(string settingsPath, Func<string, SoundFileCheck>? soundCheck = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(settingsPath);
        SettingsPath = settingsPath;
        this.soundCheck = soundCheck;
    }

    /// <inheritdoc />
    public string SettingsPath { get; }

    /// <summary>Parses settings JSON text (size-limited, strict contract) without validating ranges.</summary>
    /// <param name="json">JSON text.</param>
    /// <returns>The deserialized settings.</returns>
    /// <exception cref="InvalidDataException">The text is too large, not an object, or not valid settings JSON.</exception>
    public static AppSettings Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > SettingsLimits.MaxSettingsFileBytes)
            throw new InvalidDataException("settings file is too large");
        try
        {
            return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)
                ?? throw new InvalidDataException("settings file is empty");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"settings file is not valid JSON (line {ex.LineNumber + 1})", ex);
        }
    }

    /// <summary>Serializes settings to indented JSON.</summary>
    /// <param name="settings">Settings to serialize.</param>
    /// <returns>JSON text.</returns>
    public static string Serialize(AppSettings settings) =>
        JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);

    /// <inheritdoc />
    public SettingsLoadResult Load()
    {
        try
        {
            var fi = new FileInfo(SettingsPath);
            if (!fi.Exists) return new SettingsLoadResult(SettingsLoadStatus.Missing, null, null);
            if (fi.Length > SettingsLimits.MaxSettingsFileBytes)
                return new SettingsLoadResult(SettingsLoadStatus.Failed, null, "settings file is too large");
            var raw = Parse(File.ReadAllText(SettingsPath));
            return new SettingsLoadResult(SettingsLoadStatus.Loaded, SettingsValidator.Validate(raw, soundCheck), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new SettingsLoadResult(SettingsLoadStatus.Failed, null, ex.Message);
        }
    }

    /// <inheritdoc />
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        string tmp = SettingsPath + ".tmp";
        File.WriteAllText(tmp, Serialize(settings));
        if (File.Exists(SettingsPath)) File.Replace(tmp, SettingsPath, null);
        else File.Move(tmp, SettingsPath);
    }
}
