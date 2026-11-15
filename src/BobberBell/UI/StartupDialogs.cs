using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using BobberBell.Interop;
using BobberBell.Services;

namespace BobberBell.UI;

/// <summary>
/// Invisible, top-most owner for the startup license/trial dialogs. It gives them a taskbar button and keeps them
/// centered and in front of a fullscreen game. Used only before the main window exists, so it never steals focus
/// while the app is running.
/// </summary>
internal sealed class StartupDialogOwner : Form
{
    /// <summary>Initializes a new instance of the <see cref="StartupDialogOwner"/> class.</summary>
    public StartupDialogOwner()
    {
        Text = "BobberBell";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = true;
        TopMost = true;
        Opacity = 0;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(1, 1);
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
        Location = new Point(area.Left + (area.Width / 2), area.Top + (area.Height / 2));
        try
        {
            string iconPath = Path.Combine(AppPaths.AssetsDirectory, "BobberBell.ico");
            if (File.Exists(iconPath)) Icon = new Icon(iconPath);
        }
        catch (IOException) { }
        catch (ArgumentException) { }
    }

    /// <summary>Shows the owner and asks Windows to bring it to the foreground.</summary>
    public void ShowInFront()
    {
        Show();
        BringToFront(Handle);
    }

    /// <summary>Makes a window top-most and asks for the foreground (Windows may still only flash the taskbar button).</summary>
    /// <param name="handle">The window handle.</param>
    internal static void BringToFront(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
        NativeMethods.SetForegroundWindow(handle);
    }
}

/// <summary>The license key prompt (replaces the VB InputBox so Cancel can be told apart and the dialog can be kept in front).</summary>
internal sealed class KeyEntryForm : Form
{
    private readonly TextBox input;

    private KeyEntryForm(bool inFront)
    {
        Text = "BobberBell";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = inFront;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(420, 130);

        var label = new Label { Text = "Enter license key:", AutoSize = true, Location = new Point(12, 14) };
        input = new TextBox { Location = new Point(12, 40), Width = 396, MaxLength = 200, AccessibleName = "License key" };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(246, 88), Size = new Size(78, 28) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(330, 88), Size = new Size(78, 28) };
        Controls.AddRange(new Control[] { label, input, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
        if (inFront) Shown += (s, e) => StartupDialogOwner.BringToFront(Handle);
    }

    /// <summary>Asks for a license key.</summary>
    /// <param name="owner">The owning window.</param>
    /// <param name="inFront">Keep the prompt top-most (startup only).</param>
    /// <returns>The typed text, or <c>null</c> when the prompt was cancelled or closed.</returns>
    internal static string? Ask(IWin32Window owner, bool inFront)
    {
        using var form = new KeyEntryForm(inFront);
        return form.ShowDialog(owner) == DialogResult.OK ? form.input.Text : null;
    }
}
