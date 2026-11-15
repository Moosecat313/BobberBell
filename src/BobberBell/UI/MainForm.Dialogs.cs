using System.Drawing;
using System.IO;
using System.Windows.Forms;
using BobberBell.Core.Settings;
using BobberBell.Services;

namespace BobberBell.UI;

/// <summary>Help, license and warning dialogs (intentional user-facing UX).</summary>
internal sealed partial class MainForm
{
    private static string RtfEscape(string t)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char ch in t)
        {
            if (ch == '\\' || ch == '{' || ch == '}') sb.Append('\\').Append(ch);
            else if (ch > 127) sb.Append("\\u").Append((int)ch).Append('?');
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    private void ShowHowToDialog()
    {
        // Title, short intro, bullet points, optional tip
        var sections = new (string Title, string Intro, string[] Points, string? Tip)[]
        {
            ("Prepare the game",
             "Bind fishing to a single key (game keybinds or your addon). Buffs such as lures need a buff key and a rod key too.",
             new[] {
                 "BobberBell only presses those keys. It never installs, reads or changes anything in the game.",
                 "Keep the game's sound effects turned on." },
             null),
            ("Target sound",
             "On the Audio tab, choose what BobberBell listens for.",
             new[] {
                 "BobberSplash is the default. It loads BobberSplash1, BobberSplash2 and BobberSplash3, and any one of them counts as a bite.",
                 "Custom file uses your own recording instead.",
                 "Browse loads your own recording (WAV, MP3, FLAC or OGG).",
                 "The status line shows which sounds are loaded.",
                 "Preview plays the sound once per press. With BobberSplash, each press plays the next splash (1, 2, 3). Press it while playing to stop. Preview is off while listening." },
             "A short, clean clip of only the sound works best."),
            ("Audio output",
             "Select the speakers or headphones the game plays through (the [Loopback] entries).",
             new[] {
                 "BobberBell hears everything on that output, not your microphone.",
                 "The device is saved by name. If it's missing at startup you get a warning and the default output is used.",
                 "Keep the game volume steady and avoid music or videos on the same output." },
             null),
            ("Minimum similarity",
             "How closely a sound must resemble the target. It is not a probability.",
             new[] {
                 "Default is 0.40. Higher is stricter, lower accepts more sounds.",
                 "Calibrate it: Start in detection-only mode, let a few real bites happen and read \"peak\" next to Live similarity.",
                 "Set Minimum similarity a little below the typical peak (about 0.05 to 0.10 lower).",
                 "Other sounds triggering it? Raise it." },
             null),
            ("Minimum volume",
             "Filters out quiet or distant sounds.",
             new[] {
                 "Default is -35 dBFS. -30 needs a louder sound, -40 accepts a quieter one.",
                 "Both similarity and volume must pass for a match." },
             "Nearby players making the same sound can still match."),
            ("Fishing",
             "On the Fishing tab, choose the fishing key and the cast wait.",
             new[] {
                 "Start casts once, then listens. On a bite: a random 0.5 to 1.5 s reaction, the fishing key reels in, a random cast wait (default 1.5 to 2.5 s), then the fishing key casts again.",
                 "Each listen window lasts 40 s. If no bite is heard, one recovery cast is sent and a new window starts.",
                 "Keys: Space, Enter, Tab, Mouse 4, Mouse 5, 0 to 9, - and =, A to Z, F1 to F12, Left Ctrl, Left Shift and the arrows.",
                 "F8 is reserved for Start and Stop." },
             null),
            ("Session timer",
             "Auto-stop ends the session after a set time.",
             new[] {
                 "Turn on Auto-stop on the Fishing tab and set Stop after (default 0.25 hours, maximum 168; higher values are capped at 168).",
                 "0 means no limit and shows a one-time warning.",
                 "The time left counts down at the right of the status line." },
             null),
            ("Buffs",
             "On the Buff tab, renew a buff such as a lure on a timer.",
             new[] {
                 "Turn on Auto-refresh and set the interval in minutes.",
                 "When a refresh is due, BobberBell finishes handling the current bite first. It never buffs in the middle of a bite.",
                 "Sequence: buff key, buffing wait, rod key, buffing wait, then the cast.",
                 "Also apply on Start buffs before the first cast." },
             null),
            ("Start and stop",
             "Test first with Send inputs off (detection only) and watch the live bars and the Log.",
             new[] {
                 "Send inputs is always off when BobberBell opens. Turn it on when you're ready to automate.",
                 "After Start you have 4 s to click into the game. The window active when the countdown ends becomes the game window.",
                 "F8 starts or stops and Esc stops, from anywhere, even inside the game.",
                 "If you leave the game window, inputs pause (status shows Paused) and resume when you return." },
             null),
            ("Window name",
             "On the Window tab, set the name shown in the title bar and taskbar.",
             new[] {
                 "Default is \"" + DefaultWindowName + "\".",
                 "Random makes a random name, Apply uses it now, Save settings keeps it." },
             null),
            ("Log and settings",
             "The Log tab lists bites, casts, buffs and errors.",
             new[] {
                 "Each bite names the sound heard, e.g. \"BobberSplash2 heard · 0.812s → reel in 1\".",
                 "Lines are also written to log.txt in %LOCALAPPDATA%\\BobberBell (Open folder on the Log tab).",
                 "The Log confirms a key was sent, not that the game accepted it.",
                 "Save settings writes settings.json to %LOCALAPPDATA%\\BobberBell. It loads on the next launch.",
                 "Settings are not saved automatically on exit." },
             null),
            ("License",
             "The License button shows the version and license status.",
             new[] { "The license is stored for your Windows user, tied to this PC, and checked at launch.", "Moving to a new PC means entering the code again.", "Without a license, each PC gets a 5-minute trial (total running time). During the trial, the License button or the gold line at the bottom asks for your key." },
             null),
        };

        using var dlg = new Form
        {
            Text = ProductTitle + " - How to use",
            FormBorderStyle = FormBorderStyle.Sizable,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = true,
            ShowInTaskbar = false,
            ClientSize = new Size(680, 640),
            MinimumSize = new Size(480, 400),
            BackColor = ColBg,
            Font = Font
        };
        dlg.HandleCreated += (s2, e2) => Theme.ApplyDarkTitleBar(dlg, ColBg, ColText);
        try { dlg.Icon = Icon; } catch { }

        var title = new Label
        {
            Text = "How to use",
            ForeColor = ColGold,
            Font = new Font("Georgia", 18f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(14, 10)
        };
        dlg.Controls.Add(title);
        var subtitle = new Label
        {
            Text = ProductTitle + "  ·  quick guide",
            ForeColor = ColMuted,
            Font = new Font("Segoe UI", 9f),
            AutoSize = true,
            Location = new Point(18, 44)
        };
        dlg.Controls.Add(subtitle);
        var rule = new Panel
        {
            BackColor = ColGold,
            Location = new Point(14, 68),
            Size = new Size(652, 1),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        dlg.Controls.Add(rule);

        var rtb = new RichTextBox
        {
            Location = new Point(14, 78),
            Size = new Size(652, 506),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = ColPanel,
            ForeColor = ColText,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            DetectUrls = false,
            TabStop = false
        };

        string C(Color c) => $"\\red{c.R}\\green{c.G}\\blue{c.B};";
        var r = new System.Text.StringBuilder();
        r.Append(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}{\f1 Georgia;}}");
        r.Append("{\\colortbl ;").Append(C(ColGold)).Append(C(ColText)).Append(C(ColMuted)).Append(C(ColGoldHi)).Append('}');
        r.Append(@"\pard\li240\ri240\sa0\sb120\f0\fs20\cf2 ");
        r.Append(@"\pard\li240\ri240\sb120\sa40\f0\fs22\b\cf4 ")
         .Append(RtfEscape("Listens for a specific sound, then pushes a specified button, hands-free."))
         .Append(@"\b0\par");
        int n = 1;
        foreach (var (t, intro, points, tip) in sections)
        {
            // heading: gold Georgia, number + title
            r.Append(@"\pard\li240\ri240\sb260\sa80\f1\fs26\b\cf1 ")
             .Append(n++).Append(@".  ").Append(RtfEscape(t)).Append(@"\b0\par");
            r.Append(@"\pard\li240\ri240\sb0\sa80\f0\fs20\cf2 ").Append(RtfEscape(intro)).Append(@"\par");
            foreach (var pt in points)
                r.Append(@"\pard\li600\fi-240\ri240\sb0\sa60\f0\fs20\cf1\u8226?\tab\cf2 ")
                 .Append(RtfEscape(pt)).Append(@"\par");
            if (tip != null)
                r.Append(@"\pard\li240\ri240\sb60\sa0\f0\fs19\i\cf3 Tip: ").Append(RtfEscape(tip)).Append(@"\i0\par");
        }
        r.Append(@"\pard\sa200\par}");
        dlg.Controls.Add(rtb);
        rtb.Rtf = r.ToString();
        rtb.SelectionStart = 0;

        var close = MakeGoldOutlineButton("Close", 556, 596, 110, 34);
        close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        close.DialogResult = DialogResult.OK;
        dlg.Controls.Add(close);
        dlg.AcceptButton = close;
        dlg.CancelButton = close;
        dlg.Shown += (s, e) => close.Focus();
        dlg.ShowDialog(this);
    }

    private void ShowLicenseDialog()
    {
        using var dlg = new Form
        {
            Text = ProductTitle,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(300, 252),
            BackColor = ColPanel,
            Font = Font
        };
        dlg.HandleCreated += (s2, e2) => Theme.ApplyDarkTitleBar(dlg, ColBg, ColText);
        dlg.Controls.Add(new Label
        {
            Text = ProductTitle,
            ForeColor = ColGold,
            Font = new Font("Georgia", 13f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(10, 14),
            Size = new Size(280, 28)
        });
        var emblem = new PictureBox
        {
            Location = new Point(100, 50),
            Size = new Size(100, 100),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = ColPanel
        };
        try
        {
            string ep = Path.Combine(AppPaths.AssetsDirectory, "BobberBell_emblem_256.png");
            if (File.Exists(ep))
                using (var fs = File.OpenRead(ep)) using (var src = Image.FromStream(fs)) emblem.Image = new Bitmap(src);
        }
        catch { }
        dlg.Controls.Add(emblem);
        dlg.Controls.Add(new Label
        {
            Text = "License active.",
            ForeColor = ColGoldHi,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(10, 158),
            Size = new Size(280, 28)
        });
        var ok = MakeGoldFillButton("OK", 105, 202, 90, 32);
        ok.DialogResult = DialogResult.OK;
        dlg.Controls.Add(ok);
        dlg.AcceptButton = ok;
        dlg.CancelButton = ok;
        dlg.ShowDialog(this);
    }


    private const decimal MaxSessionHours = SettingsLimits.MaxSessionHours;
    private bool noLimitWarningShowing;
    private bool noLimitWarningShown;

    private void WarnNoSessionLimit()
    {
        if (noLimitWarningShowing) return;
        if (noLimitWarningShown) return; // once per app launch
        noLimitWarningShown = true;
        noLimitWarningShowing = true;
        try
        {
            MessageBox.Show(this,
                "Stop after is set to 0, which means no time limit.\n\n" +
                "Fishing nonstop for hours will likely lead to detection. A short session such as 0.25 hours is safer.",
                ProductTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { noLimitWarningShowing = false; }
    }
}
