using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace BobberBell
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            using var mutex = new Mutex(true, @"Local\BobberBell.SingleInstance.7F3A2C1E", out bool createdNew);
            if (!createdNew)
            {
                // Already running: bring the existing window forward instead of starting a second copy.
                try
                {
                    var me = Process.GetCurrentProcess();
                    var other = Process.GetProcessesByName(me.ProcessName).FirstOrDefault(p => p.Id != me.Id && p.MainWindowHandle != IntPtr.Zero);
                    if (other != null)
                    {
                        ShowWindow(other.MainWindowHandle, 9); // SW_RESTORE
                        SetForegroundWindow(other.MainWindowHandle);
                    }
                }
                catch { }
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string blocked = Form1.ProtectionCheck();
            if (blocked != null)
            {
                MessageBox.Show(blocked, "BobberBell", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Application.Exit();
                return;
            }

            Application.Run(new Form1());
            GC.KeepAlive(mutex);
        }
    }
}
