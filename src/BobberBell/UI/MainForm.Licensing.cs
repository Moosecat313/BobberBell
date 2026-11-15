using System;
using System.Collections.Generic;
using System.Windows.Forms;
using BobberBell.Core.Licensing;
using BobberBell.Services;
using Microsoft.VisualBasic;

namespace BobberBell.UI;

/// <summary>License prompt and the start-up anti-tamper check (both unchanged from v1.0).</summary>
internal sealed partial class MainForm
{
    private static readonly LicenseService license = new(AppPaths.LicensePath, new RegistryMachineIdProvider());

    // Stored bound license, or prompt for a key. Algorithm and file format live in LicenseService (BobberBell.Core).
    private static bool CheckLicense()
    {
        try
        {
            if (license.HasValidStoredLicense())
                return true;
        }
        catch (Exception)
        {
            // Unreadable license.dat: fall through to the key prompt, as in v1.0.
        }

        string input = Interaction.InputBox("Enter license key:", "BobberBell", "");
        switch (license.Activate(input))
        {
            case LicenseCheckResult.Activated:
                return true;
            case LicenseCheckResult.SessionOnly:
                migrationNotes.Add("License accepted for this session only (this PC's MachineGuid could not be read); the key will be asked for again next launch.");
                return true;
            case LicenseCheckResult.TooShort:
                MessageBox.Show($"That key is too short. License keys have at least {LicenseService.MinCodeLength} letters and numbers.",
                    "BobberBell", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            default:
                MessageBox.Show("Invalid license key.", "BobberBell", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
        }
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
