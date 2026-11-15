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

/// <summary>The startup choice between a license key and the trial.</summary>
internal enum StartupChoice
{
    /// <summary>Closed or cancelled.</summary>
    Cancel,

    /// <summary>Enter a license key.</summary>
    EnterKey,

    /// <summary>Start or continue the trial.</summary>
    Trial,
}

/// <summary>
/// The startup "Enter license key / Start trial" dialog. A WinForms dialog rather than a TaskDialog so it can be created
/// top-most (Windows won't let a background process raise a TaskDialog above a fullscreen game afterwards).
/// </summary>
internal sealed class TrialChoiceForm : Form
{
    private StartupChoice choice = StartupChoice.Cancel;

    private TrialChoiceForm(string trialButtonText)
    {
        Text = "BobberBell";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(500, 250);

        var heading = new Label
        {
            Text = "No license on this PC",
            Font = new Font("Segoe UI", 12f),
            ForeColor = Color.FromArgb(0, 51, 153),
            AutoSize = true,
            Location = new Point(16, 14),
        };
        var text = new Label { Text = "Enter your license key, or try BobberBell first.", AutoSize = true, Location = new Point(16, 46) };
        var key = ChoiceButton("Enter license key", "Activate BobberBell on this PC.", 76, StartupChoice.EnterKey);
        var trial = ChoiceButton(trialButtonText, "Five minutes of total running time per PC, then a license key is required.", 136, StartupChoice.Trial);
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(406, 208), Size = new Size(78, 28) };
        Controls.AddRange(new Control[] { heading, text, key, trial, cancel });
        CancelButton = cancel;
        Shown += (s, e) => StartupDialogOwner.BringToFront(Handle);
    }

    /// <summary>Shows the choice.</summary>
    /// <param name="owner">The startup owner window.</param>
    /// <param name="trialButtonText">For example "Start trial (5:00 left)".</param>
    /// <returns>What the user picked.</returns>
    internal static StartupChoice Ask(IWin32Window owner, string trialButtonText)
    {
        using var form = new TrialChoiceForm(trialButtonText);
        form.ShowDialog(owner);
        return form.choice;
    }

    private Button ChoiceButton(string title, string note, int top, StartupChoice value)
    {
        var button = new Button
        {
            Text = title + Environment.NewLine + note,
            AccessibleName = title,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(16, top),
            Size = new Size(468, 52),
            FlatStyle = FlatStyle.System,
        };
        button.Click += (s, e) =>
        {
            choice = value;
            DialogResult = DialogResult.OK;
        };
        return button;
    }
}
