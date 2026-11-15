using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using BobberBell.Core.Logging;
using BobberBell.Core.Settings;
using BobberBell.Services;
using BobberBell.UI.Controls;

namespace BobberBell.UI;

/// <summary>Builds the window: header, tab strip, the five pages and the bottom control panel.</summary>
internal sealed partial class MainForm
{
    private void BuildUi()
    {
        Controls.Clear();
        Text = DefaultWindowName;
        try
        {
            string iconPath = Path.Combine(AppPaths.AssetsDirectory, "BobberBell.ico");
            if (File.Exists(iconPath)) Icon = new Icon(iconPath);
        }
        catch { }
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MaximumSize = Size.Empty;
        ClientSize = new Size(860, 880);
        MinimumSize = SizeFromClientSize(new Size(780, 840));
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = ColBg;
        Font = new Font("Segoe UI", 9.5f);
        DoubleBuffered = true;

        // Header logo (includes stylized BobberBell wordmark)
        pictureLogo = new PictureBox
        {
            Location = new Point((ClientSize.Width - 570) / 2, 4),
            Size = new Size(570, 142),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Top
        };
        string logoPath = Path.Combine(AppPaths.AssetsDirectory, "BobberBell_logo_blend.png");
        if (File.Exists(logoPath))
        {
            pictureLogo.Image = Image.FromFile(logoPath);
        }
        else
        {
            // Fallback text if logo file is missing
            var fallback = new Label
            {
                Text = "BobberBell",
                Font = new Font("Georgia", 28f, FontStyle.Bold),
                ForeColor = ColGold,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point((ClientSize.Width - 520) / 2, 48),
                Size = new Size(520, 48),
                Anchor = AnchorStyles.Top,
                BackColor = Color.Transparent
            };
            Controls.Add(fallback);
        }
        Controls.Add(pictureLogo);

        btnLicense = MakeGoldOutlineButton("License", 740, 56, 96, 32);
        btnLicense.Click += (s, e) =>
        {
            if (trialMode)
            {
                EnterKeyDuringTrial(); // trial: the License button asks for a key
                return;
            }

            ShowLicenseDialog();
        };
        btnLicense.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        Controls.Add(btnLicense);

        var btnHowTo = MakeGoldOutlineButton("How to use", 740, 98, 96, 32);
        btnHowTo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnHowTo.Click += (s, e) => ShowHowToDialog();
        Controls.Add(btnHowTo);
        btnHowTo.BringToFront();
        btnLicense.BringToFront();

        // Tab strip
        int tabY = 152;
        tabAudio = MakeTabButton("Audio", 24, tabY, true);
        tabFishing = MakeTabButton("Fishing", 120, tabY, false);
        tabBuff = MakeTabButton("Buff", 216, tabY, false);
        tabWindow = MakeTabButton("Window", 312, tabY, false);
        tabLog = MakeTabButton("Log", 408, tabY, false);
        foreach (var t in new[] { tabAudio, tabFishing, tabBuff, tabWindow, tabLog })
        {
            var tb = t;
            tb.Click += (s, e) => ShowPage(tabPages.First(x => x.Tab == tb).Page, tb);
            Controls.Add(tb);
        }

        activeTabUnderline = new Panel
        {
            BackColor = ColGold,
            Location = new Point(24, tabY + 32),
            Size = new Size(80, 3)
        };
        activeTabUnderline.Paint += (s, e) =>
        {
            var u = activeTabUnderline;
            using var br = new LinearGradientBrush(new Rectangle(0, 0, u.Width, u.Height), ColGold, ColGold, LinearGradientMode.Horizontal)
            {
                InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.FromArgb(150, 116, 60), ColGoldHi, Color.FromArgb(150, 116, 60) },
                    Positions = new[] { 0f, 0.5f, 1f }
                }
            };
            e.Graphics.FillRectangle(br, 0, 0, u.Width, u.Height);
            using var hi = new Pen(Color.FromArgb(90, Color.White));
            e.Graphics.DrawLine(hi, 4, 0, u.Width - 5, 0);
        };
        Controls.Add(activeTabUnderline);

        // Content card
        const int bottomH = 240;
        int bottomY = ClientSize.Height - 12 - bottomH;
        var content = new Panel
        {
            Location = new Point(20, 188),
            Size = new Size(ClientSize.Width - 40, bottomY - 12 - 188),
            BackColor = ColPanel,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        MakeCard(content);
        content.Padding = new Padding(6);
        Controls.Add(content);
        pageSize = content.DisplayRectangle.Size; // build pages at their docked size so right/bottom anchors hold

        pageAudio = BuildAudioPage();
        pageFishing = BuildFishingPage();
        pageBuff = BuildBuffPage();
        pageWindow = BuildWindowPage();
        pageLog = BuildLogPage();
        tabPages = new[] { (pageAudio, tabAudio), (pageFishing, tabFishing), (pageBuff, tabBuff), (pageWindow, tabWindow), (pageLog, tabLog) };
        foreach (var tp in tabPages)
        {
            tp.Page.Parent = content;
            tp.Page.Visible = tp.Page == pageAudio;
        }

        // Bottom monitoring + controls
        panelBottom = new Panel
        {
            Location = new Point(20, bottomY),
            Size = new Size(ClientSize.Width - 40, bottomH),
            BackColor = ColPanel,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        MakeCard(panelBottom);
        Controls.Add(panelBottom);
        int bw = panelBottom.Width;

        AddMutedLabel(panelBottom, "Live similarity", 20, 16);
        barLiveSim = new MeterBar(ColCard, ColGold, ColGoldHi)
        {
            Location = new Point(20, 38),
            Size = new Size(bw - 220, 10),
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        panelBottom.Controls.Add(barLiveSim);
        lblLiveSim = new Label
        {
            Text = "—",
            ForeColor = ColText,
            Location = new Point(bw - 188, 32),
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        panelBottom.Controls.Add(lblLiveSim);
        lblPeak = new Label
        {
            Text = "peak —",
            ForeColor = ColGold,
            Location = new Point(bw - 120, 32),
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        panelBottom.Controls.Add(lblPeak);
        var tipPeak = new ToolTip();
        tipPeak.SetToolTip(lblPeak, "Highest similarity heard since Start. Set Minimum similarity a little below the typical peak of real bites.");

        AddMutedLabel(panelBottom, "Live volume", 20, 60);
        barLiveVol = new MeterBar(ColCard, Color.FromArgb(70, 130, 200), Color.FromArgb(120, 185, 245))
        {
            Location = new Point(20, 82),
            Size = new Size(bw - 220, 10),
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        panelBottom.Controls.Add(barLiveVol);
        lblLiveVol = new Label
        {
            Text = "— dBFS",
            ForeColor = ColText,
            Location = new Point(bw - 188, 76),
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        panelBottom.Controls.Add(lblLiveVol);

        chkSendInputs = new ToggleSwitch(ColCard, ColGold, ColBorder, ColText)
        {
            Text = "Send inputs  ·  off = detection only",
            ForeColor = ColText,
            Location = new Point(20, 106),
            Checked = false
        };
        panelBottom.Controls.Add(chkSendInputs);

        btnStart = MakeGoldFillButton("▶  Start", 20, 140, 144, 40);
        btnStart.Click += BtnStart_Click;
        panelBottom.Controls.Add(btnStart);

        btnStop = MakeGoldOutlineButton("■  Stop", 176, 140, 144, 40);
        btnStop.Enabled = false;
        btnStop.Click += BtnStop_Click;
        panelBottom.Controls.Add(btnStop);

        btnSave = MakeGoldFillButton("Save settings", 332, 140, 144, 40);
        btnSave.Click += BtnSave_Click;
        panelBottom.Controls.Add(btnSave);

        panelBottom.Controls.Add(new Label
        {
            Text = "F8 / Esc",
            ForeColor = ColMuted,
            Location = new Point(bw - 80, 152),
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        });

        lblStatus = new Label
        {
            Text = "Stopped — test detection first",
            ForeColor = ColGoldHi,
            Location = new Point(20, 192),
            Size = new Size(bw - 320, 20),
            AutoEllipsis = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        panelBottom.Controls.Add(lblStatus);

        lblSession = new Label
        {
            Text = "",
            ForeColor = ColGold,
            Location = new Point(bw - 300, 192),
            Size = new Size(280, 20),
            TextAlign = ContentAlignment.TopRight,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        panelBottom.Controls.Add(lblSession);

        lblFooter = new Label
        {
            Text = FooterHint,
            ForeColor = ColMuted,
            Location = new Point(20, 214),
            AutoSize = true
        };
        panelBottom.Controls.Add(lblFooter);

        KeyPreview = true;
        InstallHotkeyHook();

        if (focusTimer == null)
        {
            focusTimer = new System.Windows.Forms.Timer { Interval = 300 };
            focusTimer.Tick += (s, e) => { UpdateFocusStatus(); UpdateSessionStatus(); UpdateTrial(); };
            focusTimer.Start();
        }
    }

    private Panel BuildAudioPage()
    {
        var p = NewPage();

        AddPageHeader(p, "Audio", "Recognize the sound and filter out distant ones.");

        AddMutedLabel(p, "Target sound", 24, 90);
        comboBoxLibrary = MakeDarkCombo(24, 114, 420);
        comboBoxLibrary.SelectedIndexChanged += ComboBoxLibrary_SelectedIndexChanged;
        p.Controls.Add(comboBoxLibrary);

        btnBrowse = MakeGoldOutlineButton("Browse", 454, 110, 100, 30);
        btnBrowse.Click += BtnLoad_Click;
        p.Controls.Add(btnBrowse);

        btnPreview = MakeGoldOutlineButton("▶ Preview", 564, 110, 100, 30);
        btnPreview.Click += (s, e) => TogglePreview();
        p.Controls.Add(btnPreview);

        AddMutedLabel(p, "Game audio output", 24, 156);
        comboBoxDevice = MakeDarkCombo(24, 178, 640);
        p.Controls.Add(comboBoxDevice);

        AddMutedLabel(p, "Minimum similarity", 24, 222);
        nudSimilarity = MakeNumeric(24, 244, 120, 0.15m, 0.95m, 0.40m, 0.01m);
        p.Controls.Add(nudSimilarity);

        AddMutedLabel(p, "Minimum volume (dBFS)", 200, 222);
        nudMinVolume = MakeNumeric(200, 244, 120, -80m, 0m, -35m, 0.5m);
        p.Controls.Add(nudMinVolume);

        p.Controls.Add(new Label
        {
            Text = "Higher similarity = stricter matching. Not a probability. Volume: -30 requires a louder sound than -40.",
            ForeColor = ColMuted,
            Location = new Point(24, 286),
            Size = new Size(740, 22),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        });
        p.Controls.Add(new Label
        {
            Text = "Calibrate: Start in detection-only mode, let a few real bites happen, then read \"peak\" next to Live similarity.\nSet Minimum similarity a little below the typical peak (about 0.05–0.10 lower).",
            ForeColor = ColGold,
            Location = new Point(24, 314),
            Size = new Size(740, 40),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        });

        BuildDetectionModeSection(p, 366);
        return p;
    }

    // Detection mode (fingerprint or volume trigger) and the volume-trigger settings.
    private void BuildDetectionModeSection(Panel p, int top)
    {
        var tips = new ToolTip { AutoPopDelay = 15000 };
        AddMutedLabel(p, "Detection mode", 24, top);
        comboDetectionMode = MakeDarkCombo(24, top + 22, 440);
        comboDetectionMode.Items.AddRange(new object[] { ModeFingerprintText, ModeVolumeTriggerText });
        comboDetectionMode.SelectedIndex = 0;
        comboDetectionMode.SelectedIndexChanged += (s, e) => UpdateDetectionModeControls();
        tips.SetToolTip(comboDetectionMode,
            "Fingerprint (default): a bite is the splash matching the target sound above.\n" +
            "Volume trigger: a bite is a loud, low-pitched sound lasting a moment, after the cast.\n" +
            "Use Volume trigger when the splash never matches, e.g. on private servers whose splash differs or varies.");
        p.Controls.Add(comboDetectionMode);

        AddMutedLabel(p, "Trigger level (dBFS)", 24, top + 66);
        nudTriggerLevel = MakeNumeric(24, top + 88, 120, SettingsLimits.MinVolumeDb, SettingsLimits.MaxVolumeDb, SettingsLimits.DefaultTriggerLevelDb, 0.5m);
        tips.SetToolTip(nudTriggerLevel, "A sound counts as loud at or above this level. Watch Live volume during a bite; set this well below the splash and above the background.");
        p.Controls.Add(nudTriggerLevel);

        AddMutedLabel(p, "Min. sound length (s)", 200, top + 66);
        nudTriggerSeconds = MakeNumeric(200, top + 88, 120, SettingsLimits.MinTriggerSeconds, SettingsLimits.MaxTriggerSeconds, SettingsLimits.DefaultTriggerMinSeconds, 0.05m);
        tips.SetToolTip(nudTriggerSeconds, "How long the sound must stay loud. A splash lasts 1.5-2 s; clicks and the cast sound are shorter.");
        p.Controls.Add(nudTriggerSeconds);

        AddMutedLabel(p, "Ignore after cast (s)", 376, top + 66);
        nudTriggerIgnore = MakeNumeric(376, top + 88, 120, 0m, SettingsLimits.MaxTriggerIgnoreSeconds, SettingsLimits.DefaultTriggerIgnoreSeconds, 0.1m);
        tips.SetToolTip(nudTriggerIgnore, "Sounds in the first seconds after each cast are ignored, so the cast itself is never taken for a bite.");
        p.Controls.Add(nudTriggerIgnore);

        chkToneFilter = new ToggleSwitch(ColCard, ColGold, ColBorder, ColText)
        {
            Text = "Splash tone filter",
            ForeColor = ColText,
            Location = new Point(552, top + 90),
            Size = new Size(200, 24),
            Checked = true,
        };
        tips.SetToolTip(chkToneFilter, "Only low-pitched sounds count (spectral centroid inside the tone range in settings.json, 300-3200 Hz by default). The cast sound is brighter.");
        p.Controls.Add(chkToneFilter);

        lblModeHint = new Label
        {
            ForeColor = ColMuted,
            Location = new Point(24, top + 128),
            Size = new Size(740, 40),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        p.Controls.Add(lblModeHint);
        UpdateDetectionModeControls();
    }

    private bool VolumeTriggerMode => comboDetectionMode.SelectedIndex == 1;

    private void UpdateDetectionModeControls()
    {
        bool trigger = VolumeTriggerMode;
        nudTriggerLevel.Enabled = nudTriggerSeconds.Enabled = nudTriggerIgnore.Enabled = chkToneFilter.Enabled = trigger;
        nudSimilarity.Enabled = !trigger;
        lblModeHint.Text = trigger
            ? "Volume trigger: turn game music and ambience off. Test in detection-only mode first; ignored sounds are listed in the log with their length and tone."
            : "Fingerprint: the target sound and Minimum similarity above decide what is a bite. Switch to Volume trigger if real bites never match.";
    }

    private Panel NewPage()
    {
        var p = new Panel { Size = pageSize, BackColor = ColPanel, AutoScroll = true, AutoScrollMargin = new Size(0, 8) };
        p.Dock = DockStyle.Fill;
        Theme.ApplyDarkScrollbars(p);
        typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(p, true);
        p.Paint += (s, e) => Theme.PaintGlassBand(e.Graphics, p.Width, 6 - p.AutoScrollPosition.Y);
        p.ControlAdded += (s, e) => { if (e.Control is Label) e.Control.BackColor = Color.Transparent; };
        p.Scroll += (s, e) => p.Invalidate();
        return p;
    }

    private void AddPageHeader(Panel p, string title, string subtitle)
    {
        p.Controls.Add(new Label
        {
            Text = title,
            Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold),
            ForeColor = ColText,
            Location = new Point(24, 22),
            AutoSize = true
        });
        p.Controls.Add(new Label
        {
            Text = subtitle,
            ForeColor = ColMuted,
            Location = new Point(24, 52),
            AutoSize = true
        });
    }

    private void AddSectionHeading(Panel p, string text, int x, int y)
    {
        p.Controls.Add(new Label
        {
            Text = text.ToUpperInvariant(),
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            ForeColor = ColGold,
            Location = new Point(x, y),
            AutoSize = true
        });
        p.Controls.Add(new Panel { BackColor = Color.FromArgb(34, 58, 96), Location = new Point(x, y + 22), Size = new Size(740, 1), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right });
    }

    private NumericUpDown MakeSeconds(int x, int y, decimal min, decimal max, decimal val, int decimals, decimal inc)
    {
        var n = MakeNumeric(x, y, 110, min, max, val, inc);
        n.DecimalPlaces = decimals;
        return n;
    }

    private Panel BuildFishingPage()
    {
        var p = NewPage();
        AddPageHeader(p, "Fishing", "Reel in on a bite, then cast again.");

        AddSectionHeading(p, "Bite cycle", 24, 88);
        AddMutedLabel(p, "Fishing key", 24, 118);
        comboBoxKey = MakeDarkCombo(24, 140, 226);
        p.Controls.Add(comboBoxKey);

        AddMutedLabel(p, "Cast wait min (s)", 270, 118);
        nudCastMin = MakeSeconds(270, 140, 0.5m, 10m, 1.5m, 1, 0.1m);
        p.Controls.Add(nudCastMin);
        AddMutedLabel(p, "Cast wait max (s)", 400, 118);
        nudCastMax = MakeSeconds(400, 140, 0.5m, 10m, 2.5m, 1, 0.1m);
        p.Controls.Add(nudCastMax);
        nudCastMin.ValueChanged += (s, e) => { if (nudCastMax.Value < nudCastMin.Value) nudCastMax.Value = nudCastMin.Value; };
        nudCastMax.ValueChanged += (s, e) => { if (nudCastMin.Value > nudCastMax.Value) nudCastMin.Value = nudCastMax.Value; };

        p.Controls.Add(new Label
        {
            Text = "Start casts once, then listens. On a bite: random 0.5–1.5 s reaction, fishing key (reel in), random cast wait, fishing key (cast).\n" +
                   "No bite within 40 s: one recovery cast, then a fresh listen window.",
            ForeColor = ColMuted,
            Location = new Point(24, 178),
            Size = new Size(740, 56),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        });

        AddSectionHeading(p, "Session", 24, 240);
        chkAutoStop = new ToggleSwitch(ColCard, ColGold, ColBorder, ColText)
        {
            Text = "Auto-stop session",
            ForeColor = ColText,
            Location = new Point(24, 294),
            Size = new Size(220, 24),
            Checked = true
        };
        p.Controls.Add(chkAutoStop);
        AddMutedLabel(p, "Stop after (hours)", 270, 272);
        nudSessionHours = MakeSeconds(270, 294, 0m, MaxSessionHours, 0.25m, 2, 0.25m); // typed values above 168 are capped at 168
        p.Controls.Add(nudSessionHours);
        chkAutoStop.CheckedChanged += (s, e) =>
        {
            UpdateSessionStatus();
            if (chkAutoStop.Checked && chkAutoStop.Focused && nudSessionHours.Value == 0) WarnNoSessionLimit();
        };
        nudSessionHours.ValueChanged += (s, e) =>
        {
            UpdateSessionStatus();
            if (nudSessionHours.Value == 0 && nudSessionHours.ContainsFocus)
            {
                AppendLog("Session limit disabled (Stop after set to 0 = no limit)");
                WarnNoSessionLimit();
            }
        };

        p.Controls.Add(new Label
        {
            Text = "Max 168 h (higher values are capped at 168). 0 = no limit.\nThe countdown shows at the right of the status line. Changes apply on the next Start.",
            ForeColor = ColMuted,
            Location = new Point(24, 330),
            Size = new Size(740, 36),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        });
        return p;
    }

    private Panel BuildBuffPage()
    {
        var p = NewPage();
        AddPageHeader(p, "Buff", "Renew a buff such as a lure between catches.");

        AddSectionHeading(p, "Auto-refresh", 24, 88);
        chkBuffEnabled = new ToggleSwitch(ColCard, ColGold, ColBorder, ColText)
        {
            Text = "Auto-refresh buffs",
            ForeColor = ColText,
            Location = new Point(24, 122),
            Size = new Size(220, 24)
        };
        p.Controls.Add(chkBuffEnabled);
        chkBuffOnStart = new ToggleSwitch(ColCard, ColGold, ColBorder, ColText)
        {
            Text = "Also apply on Start",
            ForeColor = ColText,
            Location = new Point(270, 122),
            Size = new Size(220, 24)
        };
        p.Controls.Add(chkBuffOnStart);

        AddMutedLabel(p, "Every (minutes)", 24, 162);
        nudBuffInterval = MakeSeconds(24, 184, 1m, 240m, 10m, 0, 1m);
        p.Controls.Add(nudBuffInterval);
        AddMutedLabel(p, "Buff key", 154, 162);
        comboBoxBuffKey = MakeDarkCombo(154, 184, 150);
        p.Controls.Add(comboBoxBuffKey);
        AddMutedLabel(p, "Rod key", 324, 162);
        comboBoxRodKey = MakeDarkCombo(324, 184, 150);
        p.Controls.Add(comboBoxRodKey);
        AddMutedLabel(p, "Buffing wait (s)", 494, 162);
        nudBuffWait = MakeSeconds(494, 184, 0.5m, 30m, 3m, 1, 0.5m);
        p.Controls.Add(nudBuffWait);
        chkBuffEnabled.CheckedChanged += (s, e) => { buffSchedule.Reset(); UpdateSessionStatus(); };
        nudBuffInterval.ValueChanged += (s, e) => { buffSchedule.Reset(); };

        p.Controls.Add(new Label
        {
            Text = "When a refresh is due, BobberBell finishes the current catch first and never buffs mid-bite.\n" +
                   "Sequence: buff key, buffing wait, rod key, buffing wait, then the cast.",
            ForeColor = ColMuted,
            Location = new Point(24, 228),
            Size = new Size(740, 60),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        });

        lblNextBuff = new Label
        {
            Text = "Next buff: —",
            ForeColor = ColGold,
            Location = new Point(24, 298),
            AutoSize = true
        };
        p.Controls.Add(lblNextBuff);
        return p;
    }

    private Panel BuildWindowPage()
    {
        var p = NewPage();

        AddPageHeader(p, "Window", "Set the name shown in this app's title bar and taskbar.");

        AddMutedLabel(p, "Window name", 24, 90);
        txtWindowName = new TextBox
        {
            Location = new Point(24, 114),
            Size = new Size(260, 28),
            BackColor = ColCard,
            ForeColor = ColText,
            BorderStyle = BorderStyle.FixedSingle,
            Text = DefaultWindowName
        };
        p.Controls.Add(txtWindowName);

        btnRandomName = MakeGoldOutlineButton("Random", 294, 110, 80, 30);
        btnRandomName.Click += (s, e) => txtWindowName.Text = GenerateRandomName(12);
        p.Controls.Add(btnRandomName);

        btnApplyName = MakeGoldOutlineButton("Apply", 384, 110, 80, 30);
        btnApplyName.Click += (s, e) =>
        {
            string name = string.IsNullOrWhiteSpace(txtWindowName.Text) ? GenerateRandomName(12) : txtWindowName.Text.Trim();
            Text = name;
            txtWindowName.Text = name;
            AppendLog($"Window → {name}");
        };
        p.Controls.Add(btnApplyName);

        p.Controls.Add(new Label
        {
            Text = "Random generates a random name. Apply renames the window now; Save settings keeps it.",
            ForeColor = ColMuted,
            Location = new Point(24, 156),
            Size = new Size(620, 40)
        });

        return p;
    }

    private Panel BuildLogPage()
    {
        var p = NewPage();
        p.Controls.Add(new Label
        {
            Text = "Log",
            Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold),
            ForeColor = ColText,
            Location = new Point(24, 22),
            AutoSize = true
        });
        p.Controls.Add(new Label
        {
            Text = "Also saved to " + PathRedactor.ForCurrentUser().Redact(AppPaths.LogPath),
            ForeColor = ColMuted,
            Location = new Point(110, 30),
            Size = new Size(520, 20),
            AutoEllipsis = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        });
        var btnOpenLog = MakeGoldOutlineButton("Open folder", 660, 22, 110, 30);
        btnOpenLog.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnOpenLog.Click += (s, e) =>
        {
            try
            {
                Directory.CreateDirectory(AppPaths.AppDataDir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + AppPaths.AppDataDir + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { AppendLog($"Open folder failed: {ex.Message}"); }
        };
        p.Controls.Add(btnOpenLog);
        listLog = new ListBox
        {
            Location = new Point(24, 64),
            Size = new Size(746, 330),
            BackColor = ColCard,
            ForeColor = ColText,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
            HorizontalScrollbar = true,
            SelectionMode = SelectionMode.None,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Cascadia Mono", 9.5f)
        };
        Theme.ApplyDarkScrollbars(listLog);
        p.Controls.Add(listLog);
        return p;
    }

    private void ShowPage(Panel page, Button tab)
    {
        foreach (var tp in tabPages)
        {
            tp.Page.Visible = tp.Page == page;
            StyleTab(tp.Tab, tp.Tab == tab);
        }
        activeTabUnderline.Left = tab.Left;
        activeTabUnderline.Width = tab.Width;
    }

    private Button MakeTabButton(string text, int x, int y, bool active)
    {
        var b = new TabButton
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(80, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = ColBg,
            ForeColor = active ? ColGoldHi : ColMuted,
            Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = ColBg;
        b.FlatAppearance.MouseDownBackColor = ColBg;
        b.MouseEnter += (s, e) => { if (b.ForeColor != ColGoldHi) b.ForeColor = ColText; };
        b.MouseLeave += (s, e) => { if (b.ForeColor != ColGoldHi) b.ForeColor = ColMuted; };
        return b;
    }

    private void StyleTab(Button b, bool active)
    {
        b.ForeColor = active ? ColGoldHi : ColMuted;
    }

    private void AddMutedLabel(Control parent, string text, int x, int y)
    {
        parent.Controls.Add(new Label
        {
            Text = text,
            ForeColor = ColMuted,
            Location = new Point(x, y),
            AutoSize = true
        });
    }

    private void MakeCard(Panel panel)
    {
        panel.BackColor = ColBg;
        panel.Tag = "card";
        panel.ControlAdded += (s, e) => { if (e.Control is Label) e.Control.BackColor = Color.Transparent; };
        typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(panel, true);
        panel.Resize += (s, e) => panel.Invalidate();
        panel.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, panel.Width - 1.5f, panel.Height - 1.5f);
            using var path = Theme.RoundedRect(r, 10);
            using (var fill = new SolidBrush(ColPanel))
                g.FillPath(fill, path);
            Theme.PaintGlassBand(g, panel.Width, 0, path);
            using var pen = new Pen(Color.FromArgb(52, 82, 124), 1f);
            g.DrawPath(pen, path);
            using (var inner = new Pen(Color.FromArgb(40, Color.White), 1f))
                g.DrawLine(inner, 12, 1.5f, panel.Width - 12, 1.5f);
            // thin gold accent along the top edge
            using var accent = new LinearGradientBrush(new Rectangle(0, 0, panel.Width, 2),
                Color.FromArgb(0, ColGold), Color.FromArgb(150, ColGold), LinearGradientMode.Horizontal);
            var blend = new ColorBlend
            {
                Colors = new[] { Color.FromArgb(0, ColGold), Color.FromArgb(150, ColGold), Color.FromArgb(0, ColGold) },
                Positions = new[] { 0f, 0.5f, 1f }
            };
            accent.InterpolationColors = blend;
            g.FillRectangle(accent, 16, 0, panel.Width - 32, 1);
        };
    }

    private ComboBox MakeDarkCombo(int x, int y, int w)
    {
        var cb = new ComboBox
        {
            Location = new Point(x, y),
            Size = new Size(w, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = ColCard,
            ForeColor = ColText,
            FlatStyle = FlatStyle.Flat,
            DrawMode = DrawMode.OwnerDrawVariable,
            ItemHeight = 17, // closed box matches NumericUpDown/TextBox height
            MaxDropDownItems = 14
        };
        cb.MeasureItem += (s, e) => e.ItemHeight = 22; // dropdown rows
        cb.DrawItem += (s, e) =>
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var bg = new SolidBrush(sel ? Color.FromArgb(28, 52, 92) : ColCard))
                e.Graphics.FillRectangle(bg, e.Bounds);
            if (sel)
                using (var bar = new SolidBrush(ColGold))
                    e.Graphics.FillRectangle(bar, e.Bounds.X, e.Bounds.Y + 3, 2, e.Bounds.Height - 6);
            string text = cb.GetItemText(cb.Items[e.Index]) ?? string.Empty;
            var tr = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, cb.Font, tr, sel ? ColGoldHi : ColText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        return cb;
    }

    private NumericUpDown MakeNumeric(int x, int y, int w, decimal min, decimal max, decimal val, decimal inc)
    {
        return new NumericUpDown
        {
            Location = new Point(x, y),
            Size = new Size(w, 28),
            Minimum = min,
            Maximum = max,
            Value = val,
            DecimalPlaces = 2,
            Increment = inc,
            BackColor = ColCard,
            ForeColor = ColText,
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    private Button MakeGoldFillButton(string text, int x, int y, int w, int h)
    {
        return new ModernButton(true, ColGold, ColGoldHi, ColCard, Color.FromArgb(20, 16, 8))
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, h),
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
    }

    private Button MakeGoldOutlineButton(string text, int x, int y, int w, int h)
    {
        return new ModernButton(false, ColGold, ColGoldHi, ColCard, ColGoldHi)
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, h),
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
    }
}
