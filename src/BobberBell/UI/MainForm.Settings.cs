using System;
using System.Drawing;
using System.IO;
using BobberBell.Core.Settings;
using BobberBell.Services;

namespace BobberBell.UI;

/// <summary>Moves settings between the controls and <see cref="ISettingsStore"/>. All validation lives in <see cref="SettingsValidator"/>.</summary>
internal sealed partial class MainForm
{
    private AppSettings CollectSettings() => new()
    {
        SelectedKey = SelectedKey(comboBoxKey)?.Name ?? "1",
        Similarity = nudSimilarity.Value,
        MinVolume = nudMinVolume.Value,
        WindowName = txtWindowName.Text.Trim(),
        LastSoundPath = lastSoundPath ?? string.Empty,
        LibrarySound = comboBoxLibrary.SelectedItem as string ?? AllTargetsName,
        AudioDevice = SelectedDeviceName(),
        CastWaitMin = nudCastMin.Value,
        CastWaitMax = nudCastMax.Value,
        AutoStopEnabled = chkAutoStop.Checked,
        AutoStopHours = nudSessionHours.Value,
        BuffEnabled = chkBuffEnabled.Checked,
        BuffIntervalMinutes = nudBuffInterval.Value,
        BuffKey = SelectedKey(comboBoxBuffKey)?.Name ?? "2",
        RodKey = SelectedKey(comboBoxRodKey)?.Name ?? "3",
        BuffWaitSeconds = nudBuffWait.Value,
        BuffOnStart = chkBuffOnStart.Checked,
        DetectionMode = VolumeTriggerMode ? SettingsLimits.DetectionModeVolumeTrigger : SettingsLimits.DetectionModeFingerprint,
        TriggerLevelDb = nudTriggerLevel.Value,
        TriggerMinSeconds = nudTriggerSeconds.Value,
        TriggerIgnoreSeconds = nudTriggerIgnore.Value,
        TriggerToneFilter = chkToneFilter.Checked,
        TriggerMinToneHz = triggerMinToneHz,
        TriggerMaxToneHz = triggerMaxToneHz,
    };

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        try
        {
            settingsStore.Save(CollectSettings());
            AppendLog($"Settings saved → {settingsStore.SettingsPath}");
            SetStatus("Settings saved", Color.LightGreen);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Error("Save failed", ex);
            SetStatus("Save failed", Color.OrangeRed);
        }
    }

    private void LoadSettingsOnStartup(bool showMessage = false)
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsPath) && File.Exists(AppPaths.LegacySettingsPath))
            {
                Directory.CreateDirectory(AppPaths.AppDataDir);
                File.Copy(AppPaths.LegacySettingsPath, AppPaths.SettingsPath);
                AppendLog($"Settings migrated from {AppPaths.LegacySettingsPath}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Error("Settings migration failed", ex);
        }

        var result = settingsStore.Load();
        if (result.Status == SettingsLoadStatus.Missing)
        {
            if (showMessage) AppendLog("No saved settings; using defaults");
            return;
        }

        if (result.Status == SettingsLoadStatus.Failed || result.Validation == null)
        {
            logger.Error($"Settings load failed: {result.Error}");
            return;
        }

        ApplySettings(result.Validation);
        if (showMessage) AppendLog($"Settings loaded from {settingsStore.SettingsPath}");
    }

    // Values are already clamped and allow-listed by SettingsValidator; this only pushes them into the controls.
    private void ApplySettings(SettingsValidationResult validated)
    {
        var settings = validated.Settings;
        SelectKey(comboBoxKey, settings.SelectedKey);
        SelectKey(comboBoxBuffKey, settings.BuffKey);
        SelectKey(comboBoxRodKey, settings.RodKey);
        nudSimilarity.Value = settings.Similarity;
        nudMinVolume.Value = settings.MinVolume;
        nudCastMax.Value = settings.CastWaitMax;
        nudCastMin.Value = settings.CastWaitMin;
        nudSessionHours.Value = settings.AutoStopHours;
        chkAutoStop.Checked = settings.AutoStopEnabled;
        nudBuffInterval.Value = settings.BuffIntervalMinutes;
        nudBuffWait.Value = settings.BuffWaitSeconds;
        chkBuffEnabled.Checked = settings.BuffEnabled;
        chkBuffOnStart.Checked = settings.BuffOnStart;
        comboDetectionMode.SelectedIndex = settings.DetectionMode == SettingsLimits.DetectionModeFingerprint ? 1 : 0;
        nudTriggerLevel.Value = settings.TriggerLevelDb;
        nudTriggerSeconds.Value = settings.TriggerMinSeconds;
        nudTriggerIgnore.Value = settings.TriggerIgnoreSeconds;
        chkToneFilter.Checked = settings.TriggerToneFilter;
        triggerMinToneHz = settings.TriggerMinToneHz;
        triggerMaxToneHz = settings.TriggerMaxToneHz;
        UpdateDetectionModeControls();
        txtWindowName.Text = settings.WindowName;
        Text = settings.WindowName;
        lastSoundPath = settings.LastSoundPath;
        foreach (var note in validated.Notes) AppendLog(note);
        chkSendInputs.Checked = false; // never restored

        if (!string.IsNullOrWhiteSpace(settings.AudioDevice))
        {
            int idx = renderDevices.FindIndex(d => string.Equals(d.FriendlyName, settings.AudioDevice, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) comboBoxDevice.SelectedIndex = idx;
            else
            {
                startupWarning = $"Saved audio output \"{settings.AudioDevice}\" was not found.\nUsing {comboBoxDevice.Text} instead. Pick the right output on the Audio tab and Save settings.";
                AppendLog($"Warning: saved audio output \"{settings.AudioDevice}\" not found; using {comboBoxDevice.Text}");
            }
        }

        if (comboBoxLibrary.Items.Contains(settings.LibrarySound))
        {
            comboBoxLibrary.SelectedIndexChanged -= ComboBoxLibrary_SelectedIndexChanged;
            comboBoxLibrary.SelectedItem = settings.LibrarySound;
            comboBoxLibrary.SelectedIndexChanged += ComboBoxLibrary_SelectedIndexChanged;
        }
    }
}
