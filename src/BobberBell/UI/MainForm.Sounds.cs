using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using BobberBell.Core.Files;
using BobberBell.Services;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BobberBell.UI;

/// <summary>Audio output devices, the sound library, custom sound loading and preview playback.</summary>
internal sealed partial class MainForm
{
    private void PopulateDevices()
    {
        comboBoxDevice.Items.Clear();
        renderDevices.Clear();
        try
        {
            var enumerator = new MMDeviceEnumerator();
            string? defaultId = null;
            try { defaultId = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID; } catch { }
            var all = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
            foreach (var d in all.OrderBy(d => d.ID == defaultId ? 0 : 1))
            {
                renderDevices.Add(d);
                comboBoxDevice.Items.Add($"{d.FriendlyName} [Loopback]{(d.ID == defaultId ? "  · default" : "")}");
            }
        }
        catch { }
        if (comboBoxDevice.Items.Count == 0)
            comboBoxDevice.Items.Add("Default render device [Loopback]");
        comboBoxDevice.SelectedIndex = 0;
    }

    private MMDevice? SelectedDevice()
    {
        int i = comboBoxDevice.SelectedIndex;
        return i >= 0 && i < renderDevices.Count ? renderDevices[i] : null;
    }

    private string SelectedDeviceName() => SelectedDevice()?.FriendlyName ?? "";

    private static string GenerateRandomName(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var result = new char[length];
        for (int i = 0; i < length; i++)
            result[i] = chars[Jitter.Next(chars.Length)];
        return new string(result);
    }

    private List<string> CurrentPreviewPaths()
    {
        var list = loadedPaths.Where(File.Exists).ToList();
        if (list.Count > 0) return list;
        if (!string.IsNullOrEmpty(lastSoundPath) && File.Exists(lastSoundPath)) return new List<string> { lastSoundPath };
        return new List<string>();
    }

    private void TogglePreview()
    {
        if (previewOut != null) { StopPreview(); return; }
        if (isRunning)
        {
            AppendLog("Preview disabled while listening (it would be detected).");
            SetStatus("Stop listening to preview the sound", ColGoldHi);
            return;
        }
        var paths = CurrentPreviewPaths();
        if (paths.Count == 0) { AppendLog("No sound selected to preview."); return; }
        // One clip per press; with BobberSplash, each press plays the next splash in turn.
        string pick = paths[previewIndex % paths.Count];
        previewIndex = (previewIndex + 1) % paths.Count;
        previewQueue.Clear();
        previewQueue.Add(pick);
        PlayNextPreview();
    }

    private void PlayNextPreview()
    {
        if (previewQueue.Count == 0) { StopPreview(); return; }
        string path = previewQueue[0];
        previewQueue.RemoveAt(0);
        try
        {
            previewReader = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                ? new WaveFileReader(path)
                : new MediaFoundationReader(path);
            previewOut = new WaveOutEvent();
            previewOut.Init(previewReader.ToSampleProvider());
            var thisOut = previewOut;
            previewOut.PlaybackStopped += (s, e) => BeginInvoke(new Action(() =>
            {
                if (previewOut != thisOut) return; // stopped by user
                DisposePreviewPlayer();
                if (previewQueue.Count > 0) PlayNextPreview(); else StopPreview();
            }));
            previewOut.Play();
            btnPreview.Text = "■ Stop";
            AppendLog($"Preview: {Path.GetFileName(path)} ({previewReader.TotalTime.TotalSeconds:0.0}s)");
        }
        catch (Exception ex)
        {
            StopPreview();
            AppendLog($"Preview failed: {ex.Message}");
        }
    }

    private void StopPreview()
    {
        previewQueue.Clear();
        DisposePreviewPlayer();
        if (btnPreview != null) btnPreview.Text = "▶ Preview";
    }

    private void DisposePreviewPlayer()
    {
        var o = previewOut; var r = previewReader;
        previewOut = null; previewReader = null;
        try { o?.Stop(); } catch { }
        o?.Dispose();
        r?.Dispose();
    }

    private static string SoundsDirectory => AppPaths.SoundsDirectory;

    private void PopulateSoundLibrary()
    {
        comboBoxLibrary.SelectedIndexChanged -= ComboBoxLibrary_SelectedIndexChanged;
        try { FillSoundLibrary(); }
        finally { comboBoxLibrary.SelectedIndexChanged += ComboBoxLibrary_SelectedIndexChanged; }
    }

    private void FillSoundLibrary()
    {
        comboBoxLibrary.Items.Clear();
        comboBoxLibrary.Items.Add(AllTargetsName);
        comboBoxLibrary.Items.Add(CustomFileName);
        if (comboBoxLibrary.Items.Count > 1)
            comboBoxLibrary.SelectedIndex = 0;
    }

    private async Task EnsureDefaultLibrarySoundAsync()
    {
        if (comboBoxLibrary.SelectedItem is not string name) return;
        if (name == CustomFileName)
        {
            if (!string.IsNullOrEmpty(lastSoundPath) && File.Exists(lastSoundPath))
                await FingerprintFileAsync(lastSoundPath, Path.GetFileName(lastSoundPath));
            return;
        }
        await LoadLibrarySoundAsync(name);
    }

    private async void ComboBoxLibrary_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (comboBoxLibrary.SelectedItem is not string name || name == CustomFileName)
            return;
        await LoadLibrarySoundAsync(name);
    }

    private async Task LoadLibrarySoundAsync(string displayName)
    {
        if (displayName == AllTargetsName)
        {
            var all = soundLibrary
                .Select(x => (Check: SoundFileValidator.ValidateLibraryFile(SoundsDirectory, x.FileName), x.Name))
                .Where(x => x.Check.IsValid)
                .Select(x => (x.Check.FullPath, x.Name))
                .ToList();
            if (all.Count == 0) { SetStatus("Missing library files", Color.OrangeRed); return; }
            await LoadSoundsAsync(all);
            return;
        }

        var entry = soundLibrary.FirstOrDefault(s => s.Name == displayName);
        if (entry.FileName == null) return;
        var check = SoundFileValidator.ValidateLibraryFile(SoundsDirectory, entry.FileName);
        if (!check.IsValid)
        {
            SetStatus("Missing library file", Color.OrangeRed);
            return;
        }

        await FingerprintFileAsync(check.FullPath, displayName);
    }

    // Custom files are validated (type, size, no network/device paths) before any decoder sees them.
    private Task FingerprintFileAsync(string filePath, string label)
    {
        var check = SoundFileValidator.Validate(filePath);
        if (!check.IsValid)
        {
            SetStatus("Load failed", Color.OrangeRed);
            logger.Warn($"Sound file rejected ({check.Reason}): {Path.GetFileName(filePath)}");
            return Task.CompletedTask;
        }

        return LoadSoundsAsync(new List<(string, string)> { (check.FullPath, label) });
    }

    private async Task LoadSoundsAsync(List<(string Path, string Label)> sounds)
    {
        string names = string.Join(", ", sounds.Select(x => x.Label));
        bool isLibrary = sounds.Count > 0 && sounds.All(x => soundLibrary.Any(l =>
            string.Equals(Path.GetFullPath(Path.Combine(SoundsDirectory, l.FileName)), Path.GetFullPath(x.Path), StringComparison.OrdinalIgnoreCase)));
        string shownName = isLibrary ? AllTargetsName : "Custom";
        SetStatus($"Fingerprinting {shownName}…", ColGoldHi);
        try
        {
            await detector.LoadReferencesAsync(sounds);
            loadedPaths = sounds.Select(x => x.Path).ToList();
            previewIndex = 0;
            lastSoundPath = sounds.Count == 1 ? sounds[0].Path : lastSoundPath;
            SetStatus($"Loaded: {shownName}", Color.LightGreen);
            AppendLog($"Listening for → {names}");
        }
        catch (Exception ex) // Any decoder failure (corrupt or unsupported file) is reported, never fatal.
        {
            SetStatus("Load failed", Color.OrangeRed);
            logger.Error($"Sound load failed ({names})", ex);
        }
    }

    private async void BtnLoad_Click(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "Audio|*.wav;*.mp3;*.flac;*.ogg|WAV files|*.wav" };
        if (ofd.ShowDialog() != DialogResult.OK) return;

        comboBoxLibrary.SelectedIndexChanged -= ComboBoxLibrary_SelectedIndexChanged;
        int customIndex = comboBoxLibrary.Items.IndexOf(CustomFileName);
        if (customIndex >= 0) comboBoxLibrary.SelectedIndex = customIndex;
        comboBoxLibrary.SelectedIndexChanged += ComboBoxLibrary_SelectedIndexChanged;

        await FingerprintFileAsync(ofd.FileName, Path.GetFileName(ofd.FileName));
    }
}
