using System;
using System.Collections.Generic;
using System.Windows.Forms;
using BobberBell.Core.Licensing;
using BobberBell.Core.Session;
using BobberBell.Services;
using Microsoft.VisualBasic;

namespace BobberBell.UI;

/// <summary>License prompt, the per-PC trial and the start-up anti-tamper check (the last unchanged from v1.0).</summary>
internal sealed partial class MainForm
{
    private const string FooterHint = "4 s to focus the game. Inputs pause while the game window isn't active.";

    private static readonly RegistryMachineIdProvider machineId = new();
    private static readonly LicenseService license = new(AppPaths.LicensePath, machineId);
    private static readonly TrialService trial = new(
        machineId, new FileTrialRecordStore(AppPaths.TrialPath), new RegistryTrialRecordStore(), SystemTickSource.Instance);

    private bool trialMode;
    private bool trialEnding;

    private enum StartupMode
    {
        Exit,
        Licensed,
        Trial,
    }

    // Stored bound license, or (if the PC still has trial time) a choice between a key and the trial, or the key prompt.
    // Algorithm and file formats live in LicenseService / TrialService (BobberBell.Core).
    private static StartupMode CheckLicense()
    {
        try
        {
            if (license.HasValidStoredLicense())
                return StartupMode.Licensed;
        }
        catch (Exception)
        {
            // Unreadable license.dat: fall through to the key prompt, as in v1.0.
        }

        switch (trial.Load())
        {
            case TrialStatus.Tampered:
                migrationNotes.Add("Trial unavailable: the trial record on this PC failed its integrity check.");
                break;
            case TrialStatus.NoMachineId:
                migrationNotes.Add("Trial unavailable: this PC's MachineGuid could not be read, so a license key is required.");
                break;
        }

        if (!trial.IsAvailable)
            return PromptForKey(null) ? StartupMode.Licensed : StartupMode.Exit;

        while (true)
        {
            var keyButton = new TaskDialogCommandLinkButton("Enter license key", "Activate BobberBell on this PC.");
            string left = TrialService.FormatRemaining(trial.RemainingSeconds);
            var trialButton = new TaskDialogCommandLinkButton(
                trial.Status == TrialStatus.Fresh ? $"Start trial ({left} left)" : $"Continue trial ({left} left)",
                "Five minutes of total running time per PC, then a license key is required.");
            var page = new TaskDialogPage
            {
                Caption = "BobberBell",
                Heading = "No license on this PC",
                Text = "Enter your license key, or try BobberBell first.",
                Icon = TaskDialogIcon.Information,
                AllowCancel = true,
                Buttons = { keyButton, trialButton, TaskDialogButton.Cancel },
            };

            var choice = TaskDialog.ShowDialog(page);
            if (choice == keyButton)
            {
                if (PromptForKey(null)) return StartupMode.Licensed;
                continue; // wrong key: back to the choice while trial time is left
            }

            if (choice == trialButton)
            {
                trial.Start();
                return StartupMode.Trial;
            }

            return StartupMode.Exit;
        }
    }

    // The v1.0 key prompt and messages. Returns true if the key was accepted.
    private static bool PromptForKey(IWin32Window? owner)
    {
        string input = Interaction.InputBox("Enter license key:", "BobberBell", "");
        switch (license.Activate(input))
        {
            case LicenseCheckResult.Activated:
                return true;
            case LicenseCheckResult.SessionOnly:
                migrationNotes.Add("License accepted for this session only (this PC's MachineGuid could not be read); the key will be asked for again next launch.");
                return true;
            case LicenseCheckResult.TooShort:
                MessageBox.Show(owner, $"That key is too short. License keys have at least {LicenseService.MinCodeLength} letters and numbers.",
                    "BobberBell", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            default:
                MessageBox.Show(owner, "Invalid license key.", "BobberBell", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
        }
    }

    // Called by the UI timer: saves trial time every few seconds, shows what is left, and ends the trial at 0:00.
    private void UpdateTrial()
    {
        if (!trialMode || trialEnding || lblFooter == null) return;
        trial.PersistIfDue();
        string text = $"Trial: {TrialService.FormatRemaining(trial.RemainingSeconds)} left on this PC · click here to enter a license key";
        if (lblFooter.Text != text) lblFooter.Text = text;
        if (trial.IsExpired) EndTrial();
    }

    private void ShowTrialFooter()
    {
        lblFooter.ForeColor = ColGoldHi;
        lblFooter.Cursor = Cursors.Hand;
        lblFooter.Click += (s, e) => EnterKeyDuringTrial();
        UpdateTrial();
    }

    private void EnterKeyDuringTrial()
    {
        if (!trialMode || trialEnding) return;
        if (PromptForKey(this)) SwitchToLicensed();
    }

    private void SwitchToLicensed()
    {
        trial.Stop();
        trialMode = false;
        lblFooter.Text = FooterHint;
        lblFooter.ForeColor = ColMuted;
        lblFooter.Cursor = Cursors.Default;
        foreach (var note in migrationNotes) AppendLog(note);
        migrationNotes.Clear();
        AppendLog("License activated · trial ended");
    }

    private void EndTrial()
    {
        trialEnding = true;
        if (isRunning) BtnStop_Click(this, EventArgs.Empty); // stop detection and key sending first
        trial.Stop();
        AppendLog("Trial time used up → license key required");
        MessageBox.Show(this, "Your 5-minute trial on this PC has ended. Enter a license key to keep using BobberBell.",
            "BobberBell", MessageBoxButtons.OK, MessageBoxIcon.Information);
        if (PromptForKey(this))
        {
            SwitchToLicensed();
            trialEnding = false;
            return;
        }

        AppendLog("No valid license key → closing");
        Close();
    }

#nullable disable
    // Kept byte-for-byte from v1.0 (see docs/THREAT_MODEL.md, "Anti-tamper"); nullable analysis is off for this block only.
    // Returns a reason to refuse to run, or null. Called from Program.Main before the form is created.
    internal static string ProtectionCheck()
    {
        if (System.Diagnostics.Debugger.IsAttached)
            return "A debugger is attached.";

        // Exact process names (case-insensitive, no .exe). No substring matching, so e.g. NVIDIA processes never trip it.
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "dnSpy", "dnSpy-x86", "ILSpy", "dotPeek32", "dotPeek64", "de4dot", "de4dot-x64",
            "cheatengine", "cheatengine-x86_64", "cheatengine-i386", "cheatengine-x86_64-SSE4-AVX2",
            "ProcessHacker", "x64dbg", "x32dbg", "ollydbg", "ida", "ida64", "idaq", "idaq64"
        };

        string found = null;
        foreach (var proc in System.Diagnostics.Process.GetProcesses())
        {
            try { if (found == null && blocked.Contains(proc.ProcessName)) found = proc.ProcessName; } catch { }
            finally { proc.Dispose(); }
        }
        return found == null ? null : $"Please close {found} and start BobberBell again.";
    }
#nullable restore
}
