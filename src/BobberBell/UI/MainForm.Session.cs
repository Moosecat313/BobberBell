using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BobberBell.Core.Detection;
using BobberBell.Core.Input;
using BobberBell.Core.Logging;
using BobberBell.Interop;
using BobberBell.Services;

namespace BobberBell.UI;

/// <summary>The fishing session: start/stop, the cast → listen → reel-in loop, buffs, focus pause and status.</summary>
internal sealed partial class MainForm
{
    private string ListeningStatus => chkSendInputs.Checked ? "Listening… inputs ON" : "Listening… detection only";

    private async void BtnStart_Click(object? sender, EventArgs e)
    {
        if (!detector.HasReferences)
        {
            MessageBox.Show("Load a reference sound first.");
            return;
        }

        if (isRunning) return;

        StopPreview();
        gameWindow = IntPtr.Zero;
        isRunning = true;
        btnStart.Enabled = false;
        btnStop.Enabled = true;
        var previous = sessionCts;
        var cts = new CancellationTokenSource();
        sessionCts = cts;
        previous?.Dispose();
        var ct = cts.Token;

        try
        {
            for (int i = 4; i >= 1; i--)
            {
                SetStatus($"Starting in {i}… focus the game", ColGoldHi);
                await Task.Delay(1000, ct);
            }
        }
        catch (OperationCanceledException) { return; }

        gameWindow = NativeMethods.GetForegroundWindow();
        focusPausedShown = false;
        if (gameWindow == Handle || gameWindow == IntPtr.Zero)
        {
            gameWindow = IntPtr.Zero;
            AppendLog("Game window not captured: BobberBell was still active. Inputs paused until you Stop and Start with the game focused.");
        }
        else
        {
            AppendLog($"Game window: {WindowTitle(gameWindow)}");
        }

        peakSimilarity = 0;
        lblPeak.Text = "peak —";
        catches = 0;
        if (sessionTimer.Start(chkAutoStop.Checked, nudSessionHours.Value))
            AppendLog($"Auto-stop at {DateTime.Now.AddHours((double)nudSessionHours.Value):HH:mm:ss} ({nudSessionHours.Value:0.##} h)");
        UpdateSessionStatus();

        await SessionLoopAsync(ct);
    }

    private void BtnStop_Click(object? sender, EventArgs e)
    {
        isRunning = false;
        sessionCts?.Cancel();
        detector.Stop();
        sessionTimer.Reset();
        btnStart.Enabled = true;
        btnStop.Enabled = false;
        SetStatus("Stopped — test detection first", ColGoldHi);
        AppendLog(catches > 0 ? $"Stopped ({catches} bites this session)" : "Stopped");
        UpdateLiveMeters(0, -120);
        UpdateSessionStatus();
    }

    // Whole fishing session: (buff) → cast → listen → reel in → wait → (buff) → cast → ...
    private async Task SessionLoopAsync(CancellationToken ct)
    {
        try
        {
            buffSchedule.Reset();
            if (chkBuffEnabled.Checked && chkBuffOnStart.Checked)
                await BuffSequenceAsync(ct);
            await CastAsync("Cast", ct);

            while (!ct.IsCancellationRequested)
            {
                SetStatus(ListeningStatus, Color.LightGreen);
                string? hit = await ListenWindowAsync(ct);
                if (hit != null)
                {
                    catches++;
                    double react = 0.5 + Jitter.NextDouble() * 1.0;
                    await Task.Delay(TimeSpan.FromSeconds(react), ct);
                    string reel = await PressAsync(SelectedKey(comboBoxKey), false, ct);
                    AppendLog($"{hit} heard · {react:F3}s → reel in {reel}");

                    double min = (double)nudCastMin.Value, max = Math.Max(min, (double)nudCastMax.Value);
                    double wait = min + Jitter.NextDouble() * (max - min);
                    SetStatus($"Caught · casting in {wait:0.0}s", ColGoldHi);
                    await Task.Delay(TimeSpan.FromSeconds(wait), ct);
                }
                else
                {
                    AppendLog($"No bite in {ListenWindowSeconds} s → recovery cast");
                }

                // Buffs are renewed only here, between catches.
                if (buffSchedule.IsDue(chkBuffEnabled.Checked, nudBuffInterval.Value)) await BuffSequenceAsync(ct);
                await CastAsync(hit != null ? "Cast" : "Recovery cast", ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) // Any failure ends the session cleanly and is logged; the app keeps running.
        {
            logger.Error("Session error", ex);
            if (isRunning && sessionCts?.Token == ct) BtnStop_Click(this, EventArgs.Empty);
        }
    }

    private async Task CastAsync(string label, CancellationToken ct)
    {
        SetStatus($"{label}…", ColGoldHi);
        string o = await PressAsync(SelectedKey(comboBoxKey), true, ct);
        AppendLog($"{label} → {o}");
        // short settle so the cast sound itself isn't heard as a bite
        await Task.Delay(400 + Jitter.Next(300), ct);
    }

    private async Task BuffSequenceAsync(CancellationToken ct)
    {
        double wait = (double)nudBuffWait.Value;
        SetStatus("Buffing…", ColGoldHi);
        string b = await PressAsync(SelectedKey(comboBoxBuffKey), true, ct);
        await Task.Delay(TimeSpan.FromSeconds(wait), ct);
        string r = await PressAsync(SelectedKey(comboBoxRodKey), true, ct);
        await Task.Delay(TimeSpan.FromSeconds(wait), ct);
        buffSchedule.MarkApplied(nudBuffInterval.Value);
        AppendLog($"Buff → {b}, rod → {r} · next in {nudBuffInterval.Value:0} min");
    }

    // Presses a key honoring Send inputs and the focus pause. waitForFocus: wait until the game is active again.
    private async Task<string> PressAsync(KeyOption? key, bool waitForFocus, CancellationToken ct)
    {
        if (key == null) return "no key selected";
        if (!chkSendInputs.Checked) return $"{key.Name} (detect only)";
        if (!GameIsActive())
        {
            if (!waitForFocus) return $"{key.Name} skipped (game window not active)";
            while (!GameIsActive())
            {
                await Task.Delay(250, ct);
                if (!chkSendInputs.Checked) return $"{key.Name} (detect only)";
            }

            await Task.Delay(300 + Jitter.Next(300), ct);
        }

        await inputSender.PressAsync(key);
        return key.Name;
    }

    // One 40 s listen window. Returns the label of the sound heard, or null on timeout.
    private Task<string?> ListenWindowAsync(CancellationToken ct)
    {
        lastSimilarity = 0;
        var thresholds = new DetectionThresholds((float)nudSimilarity.Value, (float)nudMinVolume.Value);
        return detector.ListenAsync(SelectedDevice(), thresholds, TimeSpan.FromSeconds(ListenWindowSeconds), ct);
    }

    // Capture thread → UI thread. Ignored once stopped so a late chunk can't repaint the meters.
    private void OnLevelMeasured(float db)
    {
        if (!isRunning) return;
        lastVolumeDb = db;
        try { BeginInvoke(new Action(() => UpdateLiveMeters(lastSimilarity, lastVolumeDb))); }
        catch (InvalidOperationException) { } // window closing
    }

    private void NoteSimilarity(float sim)
    {
        lastSimilarity = sim;
        if (sim > peakSimilarity) peakSimilarity = sim;
        try { BeginInvoke(new Action(() => UpdateLiveMeters(lastSimilarity, lastVolumeDb))); }
        catch (InvalidOperationException) { } // window closing
    }

    private bool GameIsActive()
    {
        if (gameWindow == IntPtr.Zero) return false;
        return NativeMethods.GetForegroundWindow() == gameWindow;
    }

    private static string WindowTitle(IntPtr h)
    {
        var sb = new System.Text.StringBuilder(256);
        NativeMethods.GetWindowText(h, sb, sb.Capacity);
        return sb.Length > 0 ? sb.ToString() : "(untitled)";
    }

    private void UpdateFocusStatus()
    {
        if (!isRunning || !chkSendInputs.Checked || btnStart.Enabled) { focusPausedShown = false; return; }
        if (gameWindow == IntPtr.Zero && lblStatus.Text.StartsWith("Starting", StringComparison.Ordinal)) return;
        bool active = GameIsActive();
        if (!active && !focusPausedShown)
        {
            focusPausedShown = true;
            SetStatus("Paused — game window not active", Color.Orange);
            AppendLog("Inputs paused: game window not active");
        }
        else if (active && focusPausedShown)
        {
            focusPausedShown = false;
            SetStatus(ListeningStatus, Color.LightGreen);
            AppendLog("Inputs resumed: game window active");
        }
    }

    private void UpdateSessionStatus()
    {
        if (lblSession == null) return;
        if (isRunning && sessionTimer.IsExpired)
        {
            AppendLog("Session time limit reached → stopping");
            BtnStop_Click(this, EventArgs.Empty);
            return;
        }

        string text = sessionTimer.StatusText(isRunning, chkAutoStop != null && chkAutoStop.Checked, nudSessionHours.Value);
        if (lblSession.Text != text) lblSession.Text = text;

        if (lblNextBuff != null)
        {
            string nb = buffSchedule.StatusText(chkBuffEnabled.Checked, chkBuffOnStart.Checked, isRunning, nudBuffInterval.Value);
            if (lblNextBuff.Text != nb) lblNextBuff.Text = nb;
        }
    }

    private void UpdateLiveMeters(float similarity, float db)
    {
        barLiveSim.Value = DetectionThresholds.SimilarityToPercent(similarity);
        lblLiveSim.Text = similarity <= 0 ? "—" : similarity.ToString("0.00");

        // map -80..0 dBFS to 0..100
        barLiveVol.Value = DetectionThresholds.VolumeToPercent(db);
        lblLiveVol.Text = db <= -119f ? "— dBFS" : $"{db:0.0} dBFS";
        if (peakSimilarity > 0) lblPeak.Text = $"peak {peakSimilarity:0.00}";
    }

    private void SetStatus(string text, Color color)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => SetStatus(text, color))); return; }
        lblStatus.Text = text;
        lblStatus.ForeColor = color;
    }

    // All log lines go through the single IAppLogger (redacted, written to log.txt); this is the UI sink.
    private void AppendLog(string line) => logger.Info(line);

    private void OnLogEntry(object? sender, LogEntry entry)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => OnLogEntry(sender, entry))); return; }
        listLog.Items.Insert(0, $"{entry.Timestamp:HH:mm:ss}  {entry.Message}");
        while (listLog.Items.Count > 200) listLog.Items.RemoveAt(listLog.Items.Count - 1);
    }
}
