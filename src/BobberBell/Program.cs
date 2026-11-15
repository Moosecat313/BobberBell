using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using BobberBell.Interop;
using BobberBell.UI;

namespace BobberBell;

/// <summary>Process entry point: single instance, anti-tamper check, then the main window.</summary>
internal static class Program
{
    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\BobberBell.SingleInstance.7F3A2C1E", out bool createdNew);
        if (!createdNew)
        {
            // Already running: bring the existing window forward instead of starting a second copy.
            try
            {
                using var me = Process.GetCurrentProcess();
                var others = Process.GetProcessesByName(me.ProcessName);
                var other = others.FirstOrDefault(p => p.Id != me.Id && p.MainWindowHandle != IntPtr.Zero);
                if (other != null)
                {
                    NativeMethods.ShowWindow(other.MainWindowHandle, NativeMethods.SW_RESTORE);
                    NativeMethods.SetForegroundWindow(other.MainWindowHandle);
                }

                foreach (var p in others) p.Dispose();
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string? blocked = MainForm.ProtectionCheck();
        if (blocked != null)
        {
            MessageBox.Show(blocked, "BobberBell", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Application.Exit();
            return;
        }

        Application.Run(new MainForm());
        GC.KeepAlive(mutex);
    }
}
