using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using BobberBell.Core.Input;
using BobberBell.Core.Licensing;
using BobberBell.Core.Logging;
using BobberBell.Core.Session;
using BobberBell.Core.Settings;
using BobberBell.Services;
using BobberBell.UI.Controls;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BobberBell.UI;

/// <summary>
/// Main window. The form only wires UI to services: audio detection (<see cref="IAudioDetector"/>), input
/// (<see cref="IInputSender"/>), settings (<see cref="ISettingsStore"/>), session timing (<see cref="SessionTimer"/>,
/// <see cref="BuffSchedule"/>) and logging (<see cref="IAppLogger"/>). Split into partial files by concern.
/// </summary>
internal sealed partial class MainForm : Form
{
    // Release version: update these with each new release.
    private const string AppVersion = SettingsLimits.AppVersion;
    private const string ProductTitle = "BobberBell V" + AppVersion;
    private const string DefaultWindowName = SettingsLimits.DefaultWindowName;
    private const string AllTargetsName = SettingsLimits.LibraryAllTargets;
    private const string CustomFileName = SettingsLimits.LibraryCustomFile;
    private const int ListenWindowSeconds = 40;

    private static readonly List<string> migrationNotes = new();

    // Matched to the BobberBell_logo_blend.png navy backdrop (~0,11,33)
    private readonly Color ColBg = Color.FromArgb(0, 11, 33);
    private readonly Color ColPanel = Color.FromArgb(4, 18, 48);
    private readonly Color ColCard = Color.FromArgb(8, 24, 58);
    private readonly Color ColGold = Color.FromArgb(201, 162, 89);
    private readonly Color ColGoldHi = Color.FromArgb(230, 200, 130);
    private readonly Color ColText = Color.FromArgb(225, 215, 195);
    private readonly Color ColMuted = Color.FromArgb(140, 148, 165);
    private readonly Color ColBorder = Color.FromArgb(40, 70, 110);

    // ---- Services ----
    private readonly FileAppLogger logger;
    private readonly ISettingsStore settingsStore = new SettingsStore(AppPaths.SettingsPath);
    private readonly IInputSender inputSender;
    private readonly IAudioDetector detector;
    private readonly SessionTimer sessionTimer = new(SystemTickSource.Instance);
    private readonly BuffSchedule buffSchedule = new(SystemTickSource.Instance);
    private readonly GlobalHotkeyHook hotkeys;
    private readonly IContainer components = new Container();

    // ---- Controls (created in BuildUi) ----
    private ComboBox comboBoxKey = null!, comboBoxLibrary = null!, comboBoxDevice = null!, comboBoxBuffKey = null!, comboBoxRodKey = null!;
    private NumericUpDown nudSimilarity = null!, nudMinVolume = null!, nudCastMin = null!, nudCastMax = null!, nudSessionHours = null!, nudBuffInterval = null!, nudBuffWait = null!;
    private TextBox txtWindowName = null!;
    private Label lblStatus = null!, lblLiveSim = null!, lblLiveVol = null!, lblFooter = null!, lblPeak = null!, lblSession = null!, lblNextBuff = null!;
    private PictureBox pictureLogo = null!;
    private MeterBar barLiveSim = null!, barLiveVol = null!;
    private ComboBox comboDetectionMode = null!;
    private NumericUpDown nudTriggerLevel = null!, nudTriggerSeconds = null!, nudTriggerIgnore = null!;
    private CheckBox chkToneFilter = null!;
    private Label lblModeHint = null!;
    private decimal triggerMinToneHz = SettingsLimits.DefaultTriggerMinToneHz, triggerMaxToneHz = SettingsLimits.DefaultTriggerMaxToneHz;
    private const string ModeFingerprintText = "Fingerprint: match the splash sound (default)";
    private const string ModeVolumeTriggerText = "Volume trigger: loud low splash after the cast";
    private CheckBox chkSendInputs = null!, chkAutoStop = null!, chkBuffEnabled = null!, chkBuffOnStart = null!;
    private Button btnBrowse = null!, btnStart = null!, btnStop = null!, btnSave = null!, btnRandomName = null!, btnApplyName = null!, btnLicense = null!;
    private Button tabAudio = null!, tabFishing = null!, tabBuff = null!, tabWindow = null!, tabLog = null!;
    private Panel pageAudio = null!, pageFishing = null!, pageBuff = null!, pageWindow = null!, pageLog = null!, panelBottom = null!;
    private (Panel Page, Button Tab)[] tabPages = Array.Empty<(Panel, Button)>();
    private Size pageSize = new Size(808, 416);
    private ListBox listLog = null!;
    private Panel activeTabUnderline = null!;
    private Button btnPreview = null!;
    private System.Windows.Forms.Timer? focusTimer;

    // ---- State ----
    private readonly List<MMDevice> renderDevices = new();
    private readonly List<string> previewQueue = new();
    private readonly List<(string Name, string FileName)> soundLibrary = new()
    {
        ("BobberSplash1", "bobbersplash-1.wav"),
        ("BobberSplash2", "bobbersplash-2.wav"),
        ("BobberSplash3", "bobbersplash-3.wav"),
    };

    private IntPtr gameWindow = IntPtr.Zero;
    private bool focusPausedShown;
    private string? startupWarning;
    private CancellationTokenSource? sessionCts;
    private float peakSimilarity;
    private int catches;
    private bool isRunning;
    private int previewIndex;
    private List<string> loadedPaths = new();
    private string lastSoundPath = string.Empty;
    private WaveOutEvent? previewOut;
    private WaveStream? previewReader;
    private float lastSimilarity;
    private float lastVolumeDb = -120f;

    /// <summary>Creates the main window: migrates legacy data, checks the license, builds the UI and loads settings.</summary>
    public MainForm()
    {
        logger = new FileAppLogger(AppPaths.LogPath, PathRedactor.ForCurrentUser());
        inputSender = new WindowsInputSender(logger);
        detector = new LoopbackFingerprintDetector(logger);
        hotkeys = new GlobalHotkeyHook(OnGlobalHotkey);

        migrationNotes.AddRange(LegacyDataMigrator.Migrate());
        var mode = CheckLicense();
        if (mode == StartupMode.Exit)
        {
            // Write why the app is closing (including why no trial was offered) before exiting.
            logger.Info($"{ProductTitle} started");
            foreach (var note in migrationNotes) logger.Info(note);
            migrationNotes.Clear();
            Environment.Exit(0);
            return;
        }

        trialMode = mode == StartupMode.Trial;

        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(720, 780);
        Text = "BobberBell";
        BuildUi();
        logger.EntryWritten += OnLogEntry;
        detector.LevelMeasured += OnLevelMeasured;
        detector.SimilarityMeasured += NoteSimilarity;
        LoadKeyOptions();
        PopulateDevices();
        PopulateSoundLibrary();
        AppendLog($"{ProductTitle} started");
        foreach (var note in migrationNotes) AppendLog(note);
        migrationNotes.Clear();
        if (trialMode)
        {
            AppendLog($"Trial mode: {TrialService.FormatRemaining(trial.RemainingSeconds)} of running time left on this PC");
            ShowTrialFooter();
        }
        LoadSettingsOnStartup(true);
        UpdateSessionStatus();
        Shown += (s, e) =>
        {
            if (startupWarning != null)
                MessageBox.Show(this, startupWarning, ProductTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        _ = EnsureDefaultLibrarySoundAsync();
    }

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyDarkTitleBar(this, ColBg, ColText);
    }

    /// <inheritdoc />
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        isRunning = false;
        sessionCts?.Cancel();
        detector.Stop();
        focusTimer?.Stop();
        hotkeys.Dispose();
        StopPreview();
        if (trialMode) trial.Stop(); // save used trial time
        base.OnFormClosing(e);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components.Dispose();
            focusTimer?.Dispose();
            hotkeys.Dispose();
            sessionCts?.Dispose();
            DisposePreviewPlayer();
            detector.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.Dispose(disposing);
    }

    // Global F8 = start/stop, Esc = stop. Marshalled to the UI thread.
    private void OnGlobalHotkey(Keys key)
    {
        if (key == Keys.F8)
            BeginInvoke(new Action(() => { if (isRunning) BtnStop_Click(this, EventArgs.Empty); else if (btnStart.Enabled) BtnStart_Click(this, EventArgs.Empty); }));
        else if (key == Keys.Escape && isRunning)
            BeginInvoke(new Action(() => { if (isRunning) BtnStop_Click(this, EventArgs.Empty); }));
    }

    private void InstallHotkeyHook()
    {
        if (!hotkeys.Install())
            AppendLog("Global hotkeys unavailable; F8/Esc work only while BobberBell is focused.");
    }
}
