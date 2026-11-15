using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BobberBell.Core.Files;
using BobberBell.Core.Input;

namespace BobberBell.Core.Settings;

/// <summary>
/// Clamps and normalizes settings loaded from disk. The rules match v1.0 exactly; a hand-edited file can
/// never disable the session limit, bind a key outside the allow-list, or point at an unsafe sound file.
/// </summary>
public static partial class SettingsValidator
{
    /// <summary>Returns a sanitized copy of <paramref name="raw"/> plus notes about corrections.</summary>
    /// <param name="raw">Settings as deserialized (may contain nulls or out-of-range values).</param>
    /// <param name="soundCheck">Sound path validator (defaults to <see cref="SoundFileValidator.Validate"/>).</param>
    /// <returns>The sanitized settings and notes.</returns>
    public static SettingsValidationResult Validate(AppSettings raw, Func<string, SoundFileCheck>? soundCheck = null)
    {
        ArgumentNullException.ThrowIfNull(raw);
        soundCheck ??= SoundFileValidator.Validate;
        var notes = new List<string>();
        var s = new AppSettings
        {
            SelectedKey = ValidKey(raw.SelectedKey, KeyCatalog.DefaultFishingKey),
            BuffKey = ValidKey(raw.BuffKey, KeyCatalog.DefaultBuffKey),
            RodKey = ValidKey(raw.RodKey, KeyCatalog.DefaultRodKey),
            Similarity = Math.Clamp(raw.Similarity, SettingsLimits.MinSimilarity, SettingsLimits.MaxSimilarity),
            MinVolume = Math.Clamp(raw.MinVolume, SettingsLimits.MinVolumeDb, SettingsLimits.MaxVolumeDb),
            AutoStopEnabled = raw.AutoStopEnabled,
            BuffIntervalMinutes = Math.Clamp(Math.Round(raw.BuffIntervalMinutes), SettingsLimits.MinBuffInterval, SettingsLimits.MaxBuffInterval),
            BuffWaitSeconds = Math.Clamp(raw.BuffWaitSeconds, SettingsLimits.MinBuffWait, SettingsLimits.MaxBuffWait),
            BuffEnabled = raw.BuffEnabled,
            BuffOnStart = raw.BuffOnStart,
        };

        decimal cmin = Math.Clamp(raw.CastWaitMin, SettingsLimits.MinCastWait, SettingsLimits.MaxCastWait);
        decimal cmax = Math.Clamp(raw.CastWaitMax, SettingsLimits.MinCastWait, SettingsLimits.MaxCastWait);
        if (cmax < cmin) cmax = cmin;
        s.CastWaitMin = cmin;
        s.CastWaitMax = cmax;

        // A hand-edited settings.json can't silently disable the limit: >168 caps at 168, negative goes back to 0.25.
        s.AutoStopHours = ValidateSessionHours(raw.AutoStopHours, notes);

        s.WindowName = NormalizeWindowName(raw.WindowName);

        string last = raw.LastSoundPath ?? string.Empty;
        if (last.Length > 0)
        {
            var check = soundCheck(last);
            if (check.NotFound)
            {
                notes.Add($"Last sound file not found, cleared: {last}");
                last = string.Empty;
            }
            else if (!check.IsValid)
            {
                notes.Add($"Last sound file rejected ({check.Reason}), cleared");
                last = string.Empty;
            }
            else
            {
                last = check.FullPath;
            }
        }

        s.LastSoundPath = last;

        string device = StripControl(raw.AudioDevice ?? string.Empty).Trim();
        s.AudioDevice = device.Length > SettingsLimits.MaxDeviceNameLength ? string.Empty : device;

        s.DetectionMode = raw.DetectionMode is SettingsLimits.DetectionModeFingerprint
            ? SettingsLimits.DetectionModeFingerprint
            : SettingsLimits.DefaultDetectionMode;
        s.TriggerLevelDb = Math.Clamp(raw.TriggerLevelDb, SettingsLimits.MinVolumeDb, SettingsLimits.MaxVolumeDb);
        s.TriggerMinSeconds = Math.Clamp(raw.TriggerMinSeconds, SettingsLimits.MinTriggerSeconds, SettingsLimits.MaxTriggerSeconds);
        s.TriggerIgnoreSeconds = Math.Clamp(raw.TriggerIgnoreSeconds, 0m, SettingsLimits.MaxTriggerIgnoreSeconds);
        s.TriggerToneFilter = raw.TriggerToneFilter;
        decimal tmin = Math.Clamp(raw.TriggerMinToneHz, SettingsLimits.MinToneHz, SettingsLimits.MaxToneHz);
        decimal tmax = Math.Clamp(raw.TriggerMaxToneHz, SettingsLimits.MinToneHz, SettingsLimits.MaxToneHz);
        if (tmax < tmin) tmax = tmin;
        s.TriggerMinToneHz = tmin;
        s.TriggerMaxToneHz = tmax;

        s.LibrarySound = raw.LibrarySound is SettingsLimits.LibraryAllTargets or SettingsLimits.LibraryCustomFile
            ? raw.LibrarySound
            : SettingsLimits.LibraryAllTargets;
        return new SettingsValidationResult(s, notes);
    }

    /// <summary>Applies the session-length rule: above 168 h caps at 168, negative becomes 0.25, 0 means no limit.</summary>
    /// <param name="hours">Saved value.</param>
    /// <param name="notes">Receives a note when the value was corrected.</param>
    /// <returns>The value to use.</returns>
    public static decimal ValidateSessionHours(decimal hours, ICollection<string>? notes = null)
    {
        if (hours > SettingsLimits.MaxSessionHours)
        {
            notes?.Add(string.Format(CultureInfo.CurrentCulture, "Saved Stop after {0:0.##} h is above {1:0} h; capped at {1:0} h", hours, SettingsLimits.MaxSessionHours));
            return SettingsLimits.MaxSessionHours;
        }

        if (hours < 0m)
        {
            notes?.Add(string.Format(CultureInfo.CurrentCulture, "Saved Stop after {0:0.##} h is invalid; using {1:0.##} h", hours, SettingsLimits.DefaultSessionHours));
            return SettingsLimits.DefaultSessionHours;
        }

        return hours;
    }

    /// <summary>Old default titles ("WeatherBug - v1.x", "BobberBell - v1.0", ...) and blanks follow the current default; max 128 chars.</summary>
    /// <param name="name">Saved window name.</param>
    /// <returns>The title to use.</returns>
    public static string NormalizeWindowName(string? name)
    {
        string trimmed = StripControl(name ?? string.Empty).Trim();
        if (trimmed.Length == 0 || OldDefaultName().IsMatch(trimmed)) return SettingsLimits.DefaultWindowName;
        return trimmed.Length > SettingsLimits.MaxWindowNameLength ? trimmed[..SettingsLimits.MaxWindowNameLength] : trimmed;
    }

    private static string ValidKey(string? name, string fallback) => KeyCatalog.Find(name)?.Name ?? fallback;

    private static string StripControl(string text) =>
        text.Any(char.IsControl) ? new string(text.Where(c => !char.IsControl(c)).ToArray()) : text;

    [GeneratedRegex(@"^(WeatherBug|BobberBell)( - v\d+(\.\d+)*)?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex OldDefaultName();
}
