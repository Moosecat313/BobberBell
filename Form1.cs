using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using WindowsInput;
using Microsoft.VisualBasic;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SoundFingerprinting;
using SoundFingerprinting.Audio;
using SoundFingerprinting.Audio.NAudio;
using SoundFingerprinting.Builder;
using SoundFingerprinting.Data;
using SoundFingerprinting.InMemory;
using SoundFingerprinting.Query;

namespace BobberBell
{
    public partial class Form1 : Form
    {
        private readonly InputSimulator inputSim = new();

        // ---- Global hotkeys (low-level keyboard hook; keys still pass through to the game) ----
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int LLKHF_INJECTED = 0x10;
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        // ---- Keyboard output: SendInput with hardware scan codes (compatibility; injected flag is left as-is) ----
        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT { public uint type; public INPUTUNION u; }
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
        private const uint INPUT_KEYBOARD = 1, KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_SCANCODE = 0x0008, MAPVK_VK_TO_VSC = 0;

        private static bool IsExtendedKey(VirtualKeyCode k) => k switch
        {
            VirtualKeyCode.UP or VirtualKeyCode.DOWN or VirtualKeyCode.LEFT or VirtualKeyCode.RIGHT or
            VirtualKeyCode.INSERT or VirtualKeyCode.DELETE or VirtualKeyCode.HOME or VirtualKeyCode.END or
            VirtualKeyCode.PRIOR or VirtualKeyCode.NEXT or VirtualKeyCode.RCONTROL or VirtualKeyCode.RMENU or
            VirtualKeyCode.DIVIDE or VirtualKeyCode.NUMLOCK => true,
            _ => false
        };

        private static bool SendScanKey(VirtualKeyCode vk, bool keyUp)
        {
            ushort scan = (ushort)MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC);
            if (scan == 0) return false;
            uint flags = KEYEVENTF_SCANCODE | (IsExtendedKey(vk) ? KEYEVENTF_EXTENDEDKEY : 0) | (keyUp ? KEYEVENTF_KEYUP : 0);
            var inputs = new[] { new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = flags } } } };
            return SendInput(1, inputs, Marshal.SizeOf<INPUT>()) == 1;
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hWnd, string app, string idList);

        internal static void ApplyDarkScrollbars(Control c)
        {
            void apply() { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } }
            if (c.IsHandleCreated) apply(); else c.HandleCreated += (s, e) => apply();
        }

        private static void ApplyDarkTitleBar(Form f, Color caption, Color text)
        {
            try
            {
                int on = 1;
                DwmSetWindowAttribute(f.Handle, 20, ref on, 4);           // immersive dark mode
                int cap = caption.R | (caption.G << 8) | (caption.B << 16);
                DwmSetWindowAttribute(f.Handle, 35, ref cap, 4);          // caption color (Win11)
                int txt = text.R | (text.G << 8) | (text.B << 16);
                DwmSetWindowAttribute(f.Handle, 36, ref txt, 4);          // title text color (Win11)
                int corner = 2;
                DwmSetWindowAttribute(f.Handle, 33, ref corner, 4);       // rounded corners
            }
            catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyDarkTitleBar(this, ColBg, ColText);
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder sb, int max);

        private LowLevelKeyboardProc hookProc;
        private IntPtr hookHandle = IntPtr.Zero;
        private IntPtr gameWindow = IntPtr.Zero;
        private System.Windows.Forms.Timer focusTimer;
        private bool focusPausedShown;
        private readonly Random rng = new();
        private readonly InMemoryModelService modelService = new InMemoryModelService();
        private readonly IAudioService audioService = new NAudioService();
        // Release version: update these with each new release.
        private const string AppVersion = "1.0";
        private const string ProductTitle = "BobberBell V" + AppVersion;
        private const string DefaultWindowName = "BobberBell - v" + AppVersion;

        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BobberBell");
        // Pre-rename data folders (WeatherBug), migrated once on first run.
        private static readonly string LegacyLocalDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeatherBug");
        private static readonly string LegacyRoamingDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WeatherBug");
        private static readonly List<string> migrationNotes = new();
        private static readonly string settingsPath = Path.Combine(AppDataDir, "settings.json");
        private static readonly string legacySettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private static readonly string logPath = Path.Combine(AppDataDir, "log.txt");
        private static readonly object logLock = new();
        private const int ListenWindowSeconds = 40;

        // Matched to the BobberBell_logo_blend.png navy backdrop (~0,11,33)
        private readonly Color ColBg = Color.FromArgb(0, 11, 33);
        private readonly Color ColPanel = Color.FromArgb(4, 18, 48);
        private readonly Color ColCard = Color.FromArgb(8, 24, 58);
        private readonly Color ColGold = Color.FromArgb(201, 162, 89);
        private readonly Color ColGoldHi = Color.FromArgb(230, 200, 130);
        private readonly Color ColText = Color.FromArgb(225, 215, 195);
        private readonly Color ColMuted = Color.FromArgb(140, 148, 165);
        private readonly Color ColBorder = Color.FromArgb(40, 70, 110);

        private ComboBox comboBoxKey, comboBoxLibrary, comboBoxDevice, comboBoxBuffKey, comboBoxRodKey;
        private NumericUpDown nudSimilarity, nudMinVolume, nudCastMin, nudCastMax, nudSessionHours, nudBuffInterval, nudBuffWait;
        private TextBox txtWindowName;
        private Label lblStatus, lblLiveSim, lblLiveVol, lblFooter, lblPeak, lblSession, lblNextBuff;
        private PictureBox pictureLogo;
        private MeterBar barLiveSim, barLiveVol;
        private CheckBox chkSendInputs, chkAutoStop, chkBuffEnabled, chkBuffOnStart;
        private Button btnBrowse, btnStart, btnStop, btnSave, btnRandomName, btnApplyName, btnLicense;
        private Button tabAudio, tabFishing, tabBuff, tabWindow, tabLog;
        private Panel pageAudio, pageFishing, pageBuff, pageWindow, pageLog, panelBottom;
        private (Panel Page, Button Tab)[] tabPages;
        private Size pageSize = new Size(808, 416);
        private readonly List<MMDevice> renderDevices = new();
        private string startupWarning;
        private CancellationTokenSource sessionCts;
        private TaskCompletionSource<string> currentBite;
        // Monotonic deadlines (Environment.TickCount64, ms) so clock changes don't affect timers.
        private long? sessionEndTick;
        private long? nextBuffTick;
        private static long NowTick => Environment.TickCount64;
        private float peakSimilarity;
        private volatile float simThreshold = 0.40f;
        private volatile float volThreshold = -35f;
        private int catches;
        private ListBox listLog;
        private Panel activeTabUnderline;

        private readonly List<(string Name, string FileName)> soundLibrary = new()
        {
            ("BobberSplash1", "bobbersplash-1.wav"),
            ("BobberSplash2", "bobbersplash-2.wav"),
            ("BobberSplash3", "bobbersplash-3.wav"),
        };

        private CancellationTokenSource listenCts;
        private bool isRunning;
        private string referenceTrackId;
        private const string AllTargetsName = "BobberSplash";
        private readonly Dictionary<string, string> activeTracks = new(); // trackId -> label
        private readonly List<string> previewQueue = new();
        private int previewIndex;
        private List<string> loadedPaths = new();
        private string lastSoundPath;
        private Button btnPreview;
        private WaveOutEvent previewOut;
        private WaveStream previewReader;
        private WasapiLoopbackCapture loopback;
        private BlockingCollection<AudioSamples> realtimeSamples = new();
        private float lastSimilarity;
        private float lastVolumeDb = -120f;

        // License codes are stored only as salted PBKDF2-SHA256 hashes (no plaintext codes in the assembly).
        private const string LicenseSalt = "3FD9657A00F04F4E3DAC3431AA18AD6F";
        private const int LicenseIterations = 200000;
        private static readonly string[] LicenseHashes =
        {
            "8A1DA96D62E1DE28EF6FC99F67C27C109BD31E06C19BFFC1B7FAACA427A7A253",
            "CEE35C6CC2F360802616EC42C1A9941DE66D5C5E81CE6140F2AE82631CDD7354",
            "BD283C38A385D88BBF29DB6BF52055A972631E865D396A60CA1BE04CA4D11C25",
            "4C0B7465F73412CCAFA887580802B6E9D2F46CA959BEDF1433B6C3839050367F",
            "80F5D7F4D3CC533E1D62CDE0FE6BF8B7252033DAA1373156FFF49C41157F756C",
            "36CA17B6A78217A70CCD396F731F4C3D01B415AA9808561D65A84D022738CFBB",
            "055FFE46AA40588B6AE4A9CDEC851E8468A4139704A0BC111E4D367123B604C1",
            "7EC8936F5A6700C4FD35812948901E4B0A65CF8A9944F253D528E29151195340",
            "A610BD83FF5BAA35DD4F3F91F17F57104AA2AABF790FB1AD88B999D3CA482D5D",
            "9298B5B64339C4DC5361C33D97E40E145DD4F17F8EC9A8307721634398DCCBD6",
            "E5075A7AD66AAFD1A952870988B3A22E09613D95BBD63B153E75AB9FBDCAB4BD"
        };
        private static readonly string LicensePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BobberBell", "license.dat");

        // After the rename: copy data from the WeatherBug folders (exact file names only, never overwriting),
        // then rename each old folder to WeatherBug.migrated so stale copies don't pile up. Logged once as one line.
        private static void MigrateLegacyData()
        {
            var copied = new List<string>();
            var renamed = new List<string>();
            var failed = new List<string>();
            try
            {
                if (Directory.Exists(LegacyLocalDir))
                {
                    Directory.CreateDirectory(AppDataDir);
                    foreach (var name in new[] { "settings.json", "log.txt" })
                    {
                        string src = Path.Combine(LegacyLocalDir, name), dst = Path.Combine(AppDataDir, name);
                        if (File.Exists(src) && !File.Exists(dst)) { File.Copy(src, dst); copied.Add(name); }
                    }
                    renamed.Add(RetireLegacyDir(LegacyLocalDir));
                }
            }
            catch (Exception ex) { failed.Add($"settings/log: {ex.Message}"); }
            try
            {
                if (Directory.Exists(LegacyRoamingDir))
                {
                    string src = Path.Combine(LegacyRoamingDir, "license.dat");
                    if (File.Exists(src) && !File.Exists(LicensePath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(LicensePath)!);
                        File.Copy(src, LicensePath);
                        copied.Add("license.dat");
                    }
                    renamed.Add(RetireLegacyDir(LegacyRoamingDir));
                }
            }
            catch (Exception ex) { failed.Add($"license: {ex.Message}"); }

            if (copied.Count > 0 || renamed.Count > 0)
                migrationNotes.Add("WeatherBug data migrated" +
                    (copied.Count > 0 ? $" (copied {string.Join(", ", copied)})" : "") +
                    (renamed.Count > 0 ? $"; old folders renamed: {string.Join(", ", renamed)}" : ""));
            foreach (var f in failed) migrationNotes.Add($"WeatherBug migration failed ({f})");
        }

        // Renames an old data folder to WeatherBug.migrated (or WeatherBug.migrated.2, .3, ...). Returns the new path.
        private static string RetireLegacyDir(string dir)
        {
            string parent = Path.GetDirectoryName(dir)!;
            string target = Path.Combine(parent, "WeatherBug.migrated");
            for (int n = 2; Directory.Exists(target) || File.Exists(target); n++)
                target = Path.Combine(parent, $"WeatherBug.migrated.{n}");
            Directory.Move(dir, target);
            return target;
        }

        // Log lines never contain the Windows user name: known roots are shown as %LOCALAPPDATA%, %APPDATA% or ~.
        private static readonly (string Root, string Label)[] RedactRoots = new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~"),
        }.Where(r => !string.IsNullOrEmpty(r.Item1)).OrderByDescending(r => r.Item1.Length).ToArray();

        internal static string RedactPaths(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            foreach (var (root, label) in RedactRoots)
                text = text.Replace(root.TrimEnd('\\'), label, StringComparison.OrdinalIgnoreCase);
            string user = Environment.UserName;
            if (!string.IsNullOrEmpty(user) && user.Length >= 3)
                text = text.Replace(@"\Users\" + user, @"\Users\~", StringComparison.OrdinalIgnoreCase);
            return text;
        }

        public Form1()
        {
            MigrateLegacyData();
            if (!CheckLicense())
            {
                Environment.Exit(0);
                return;
            }

            InitializeComponent();
            BuildUi();
            LoadKeyOptions();
            PopulateDevices();
            PopulateSoundLibrary();
            AppendLog($"{ProductTitle} started");
            foreach (var note in migrationNotes) AppendLog(note);
            LoadSettingsOnStartup(true);
            UpdateSessionStatus();
            Shown += (s, e) =>
            {
                if (startupWarning != null)
                    MessageBox.Show(this, startupWarning, ProductTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            _ = EnsureDefaultLibrarySoundAsync();
        }

        // Issued codes are 16+ letters/digits after normalizing; anything shorter is rejected before hashing.
        private const int MinCodeLength = 16;
        private static bool licenseSessionOnly;

        private static string NormalizeCode(string code) =>
            new string((code ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        private static byte[] DeriveCodeHash(string code)
        {
            string n = NormalizeCode(code);
            if (n.Length == 0) return null;
            return Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(n), Convert.FromHexString(LicenseSalt),
                LicenseIterations, HashAlgorithmName.SHA256, 32);
        }

        private static bool IsKnownHash(byte[] hash)
        {
            if (hash == null || hash.Length != 32) return false;
            bool ok = false;
            foreach (var hex in LicenseHashes)
                ok |= CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(hex));
            return ok;
        }

        private static string MachineId()
        {
            try
            {
                using var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
                using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                return (key?.GetValue("MachineGuid") as string ?? "").Trim().ToUpperInvariant();
            }
            catch { return ""; }
        }

        // Binds a license to this PC: HMAC-SHA256(code hash, MachineGuid).
        private static byte[] LicenseBinding(byte[] codeHash) =>
            HMACSHA256.HashData(codeHash, Encoding.UTF8.GetBytes(LegacyBindingLiteral + MachineId()));

        // DO NOT CHANGE this literal. It is part of the HMAC input that binds license.dat to a PC, so every friend's
        // existing license.dat was written with it; changing it (even to "BobberBell...") would invalidate them all
        // and force everyone to re-enter their code. If a new label is ever needed: accept both the old and the new
        // binding when checking, and rewrite license.dat with the new label after the next successful check.
        private const string LegacyBindingLiteral = "WeatherBug.License.v2|";
        private const string LicensePrefix = "WB2";

        private static void WriteLicense(byte[] codeHash)
        {
            // Never save an unbound license: without a MachineGuid the file would work on any PC.
            if (MachineId().Length == 0) { licenseSessionOnly = true; return; }
            Directory.CreateDirectory(Path.GetDirectoryName(LicensePath)!);
            string text = $"{LicensePrefix}|{Convert.ToHexString(codeHash)}|{Convert.ToHexString(LicenseBinding(codeHash))}";
            string tmp = LicensePath + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, LicensePath, true);
        }

        // Valid bound license for this PC, or a valid old-format (V1.0 plaintext) file, which is migrated in place.
        private static bool StoredLicenseValid()
        {
            if (!File.Exists(LicensePath)) return false;
            string text = File.ReadAllText(LicensePath).Trim();
            var parts = text.Split('|');
            if (parts.Length == 3 && parts[0] == LicensePrefix)
            {
                try
                {
                    byte[] codeHash = Convert.FromHexString(parts[1]);
                    byte[] binding = Convert.FromHexString(parts[2]);
                    if (MachineId().Length == 0) return false; // can't verify the binding: ask for the code instead
                    return IsKnownHash(codeHash) && CryptographicOperations.FixedTimeEquals(binding, LicenseBinding(codeHash));
                }
                catch { return false; }
            }
            byte[] legacy = DeriveCodeHash(text);
            if (!IsKnownHash(legacy)) return false;
            try { WriteLicense(legacy); } catch { }
            return true;
        }

        private static bool CheckLicense()
        {
            try
            {
                if (StoredLicenseValid())
                    return true;
            }
            catch { }

            string input = Interaction.InputBox("Enter license key:", "BobberBell", "");
            int len = NormalizeCode(input).Length;
            if (len > 0 && len < MinCodeLength)
            {
                MessageBox.Show($"That key is too short. License keys have at least {MinCodeLength} letters and numbers.",
                    "BobberBell", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            byte[] hash = DeriveCodeHash(input);
            if (IsKnownHash(hash))
            {
                try { WriteLicense(hash); } catch { }
                if (licenseSessionOnly)
                    migrationNotes.Add("License accepted for this session only (this PC's MachineGuid could not be read); the key will be asked for again next launch.");
                return true;
            }

            MessageBox.Show("Invalid license key.", "BobberBell", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

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

        private void BuildUi()
        {
            Controls.Clear();
            Text = DefaultWindowName;
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "BobberBell.ico");
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
            string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "BobberBell_logo_blend.png");
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
                Text = "4 s to focus the game. Inputs pause while the game window isn't active.",
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
                focusTimer.Tick += (s, e) => { UpdateFocusStatus(); UpdateSessionStatus(); };
                focusTimer.Start();
            }
        }

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
            var sections = new (string Title, string Intro, string[] Points, string Tip)[]
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
                     "When a refresh is due, BobberBell finishes the current catch first. It never buffs in the middle of a bite.",
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
                 new[] { "The license is stored for your Windows user, tied to this PC, and checked at launch.", "Moving to a new PC means entering the code again." },
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
            dlg.HandleCreated += (s2, e2) => ApplyDarkTitleBar(dlg, ColBg, ColText);
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
            dlg.HandleCreated += (s2, e2) => ApplyDarkTitleBar(dlg, ColBg, ColText);
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
                string ep = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "BobberBell_emblem_256.png");
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

            return p;
        }

        private Panel NewPage()
        {
            var p = new Panel { Size = pageSize, BackColor = ColPanel, AutoScroll = true, AutoScrollMargin = new Size(0, 8) };
            p.Dock = DockStyle.Fill;
            ApplyDarkScrollbars(p);
            typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(p, true);
            p.Paint += (s, e) => PaintGlassBand(e.Graphics, p.Width, 6 - p.AutoScrollPosition.Y);
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

        private const decimal MaxSessionHours = 168m;
        private const decimal DefaultSessionHours = 0.25m;
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
            chkBuffEnabled.CheckedChanged += (s, e) => { nextBuffTick = null; UpdateSessionStatus(); };
            nudBuffInterval.ValueChanged += (s, e) => { nextBuffTick = null; };

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
                Text = "Also saved to " + RedactPaths(logPath),
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
                    Directory.CreateDirectory(AppDataDir);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + AppDataDir + "\"") { UseShellExecute = true });
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
            ApplyDarkScrollbars(listLog);
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

        internal static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Max(1f, radius * 2);
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static Color SurfaceOf(Control c)
        {
            while (c != null && c.BackColor == Color.Transparent) c = c.Parent;
            if (c == null) return Color.Black;
            return Equals(c.Tag, "card") ? Color.FromArgb(4, 18, 48) : c.BackColor;
        }

        // Fake glassmorphism: a faint white gradient over the top of a card (no blur). Pages draw the same band,
        // shifted by their offset inside the card, so the band runs seamlessly behind transparent labels.
        internal const int GlassBandHeight = 70;
        internal static void PaintGlassBand(Graphics g, int width, int offsetY, GraphicsPath clip = null)
        {
            var band = new Rectangle(0, -offsetY, width, GlassBandHeight);
            if (band.Bottom <= 0) return;
            var state = g.Save();
            if (clip != null) g.SetClip(clip, CombineMode.Intersect);
            using (var br = new LinearGradientBrush(new Rectangle(band.X, band.Y - 1, band.Width, band.Height + 2),
                       Color.FromArgb(16, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.Vertical))
                g.FillRectangle(br, band);
            g.Restore(state);
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
                using var path = RoundedRect(r, 10);
                using (var fill = new SolidBrush(ColPanel))
                    g.FillPath(fill, path);
                PaintGlassBand(g, panel.Width, 0, path);
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
                string text = cb.GetItemText(cb.Items[e.Index]);
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

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F8)
            {
                if (isRunning) BtnStop_Click(sender, e);
                else BtnStart_Click(sender, e);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape && isRunning)
            {
                BtnStop_Click(sender, e);
                e.Handled = true;
            }
        }

        private sealed class KeyOption
        {
            public string Name { get; init; }
            public VirtualKeyCode Code { get; init; }
            public override string ToString() => Name;
        }

        // One shared key list for the fishing, buff and rod keys.
        private static readonly KeyOption[] KeyOptions = BuildKeyOptions();

        private static KeyOption[] BuildKeyOptions()
        {
            var list = new List<KeyOption>
            {
                new() { Name = "Space", Code = VirtualKeyCode.SPACE },
                new() { Name = "Enter", Code = VirtualKeyCode.RETURN },
                new() { Name = "Escape", Code = VirtualKeyCode.ESCAPE },
                new() { Name = "Tab", Code = VirtualKeyCode.TAB },
                new() { Name = "Mouse 4", Code = VirtualKeyCode.XBUTTON1 },
                new() { Name = "Mouse 5", Code = VirtualKeyCode.XBUTTON2 },
            };
            for (int i = 1; i <= 9; i++) list.Add(new() { Name = i.ToString(), Code = (VirtualKeyCode)((int)VirtualKeyCode.VK_0 + i) });
            list.Add(new() { Name = "0", Code = VirtualKeyCode.VK_0 });
            list.Add(new() { Name = "-", Code = VirtualKeyCode.OEM_MINUS });
            list.Add(new() { Name = "=", Code = VirtualKeyCode.OEM_PLUS });
            for (char c = 'A'; c <= 'Z'; c++) list.Add(new() { Name = c.ToString(), Code = (VirtualKeyCode)((int)VirtualKeyCode.VK_A + (c - 'A')) });
            for (int i = 1; i <= 12; i++) list.Add(new() { Name = "F" + i, Code = (VirtualKeyCode)((int)VirtualKeyCode.F1 + i - 1) });
            list.Add(new() { Name = "Left Ctrl", Code = VirtualKeyCode.LCONTROL });
            list.Add(new() { Name = "Left Shift", Code = VirtualKeyCode.LSHIFT });
            list.Add(new() { Name = "Up", Code = VirtualKeyCode.UP });
            list.Add(new() { Name = "Down", Code = VirtualKeyCode.DOWN });
            list.Add(new() { Name = "Left", Code = VirtualKeyCode.LEFT });
            list.Add(new() { Name = "Right", Code = VirtualKeyCode.RIGHT });
            return list.ToArray();
        }

        private void LoadKeyOptions()
        {
            foreach (var (cb, def) in new[] { (comboBoxKey, "1"), (comboBoxBuffKey, "2"), (comboBoxRodKey, "3") })
            {
                cb.Items.Clear();
                cb.Items.AddRange(KeyOptions);
                SelectKey(cb, def);
            }
        }

        private static bool SelectKey(ComboBox cb, string name)
        {
            for (int i = 0; i < cb.Items.Count; i++)
                if (cb.Items[i] is KeyOption k && string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    cb.SelectedIndex = i;
                    return true;
                }
            return false;
        }

        private static KeyOption SelectedKey(ComboBox cb) => cb.SelectedItem as KeyOption;

        private void PopulateDevices()
        {
            comboBoxDevice.Items.Clear();
            renderDevices.Clear();
            try
            {
                var enumerator = new MMDeviceEnumerator();
                string defaultId = null;
                try { defaultId = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID; } catch { }
                var all = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
                foreach (var d in all.OrderBy(d => d.ID == defaultId ? 0 : 1))
                {
                    renderDevices.Add(d);
                    comboBoxDevice.Items.Add($"{d.FriendlyName} [Loopback]{(d.ID == defaultId ? "  · default" : "")}");
                }
            }
            catch { }
            if (comboBoxDevice.Items.Count == 0)
                comboBoxDevice.Items.Add("Default render device [Loopback]");
            comboBoxDevice.SelectedIndex = 0;
        }

        private MMDevice SelectedDevice()
        {
            int i = comboBoxDevice.SelectedIndex;
            return i >= 0 && i < renderDevices.Count ? renderDevices[i] : null;
        }

        private string SelectedDeviceName() => SelectedDevice()?.FriendlyName ?? "";

        private static string GenerateRandomName(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var result = new char[length];
            var r = new Random();
            for (int i = 0; i < length; i++)
                result[i] = chars[r.Next(chars.Length)];
            return new string(result);
        }

        private List<string> CurrentPreviewPaths()
        {
            var list = loadedPaths.Where(File.Exists).ToList();
            if (list.Count > 0) return list;
            if (!string.IsNullOrEmpty(lastSoundPath) && File.Exists(lastSoundPath)) return new List<string> { lastSoundPath };
            return new List<string>();
        }

        private void TogglePreview()
        {
            if (previewOut != null) { StopPreview(); return; }
            if (isRunning)
            {
                AppendLog("Preview disabled while listening (it would be detected).");
                SetStatus("Stop listening to preview the sound", ColGoldHi);
                return;
            }
            var paths = CurrentPreviewPaths();
            if (paths.Count == 0) { AppendLog("No sound selected to preview."); return; }
            // One clip per press; with BobberSplash, each press plays the next splash in turn.
            string pick = paths[previewIndex % paths.Count];
            previewIndex = (previewIndex + 1) % paths.Count;
            previewQueue.Clear();
            previewQueue.Add(pick);
            PlayNextPreview();
        }

        private void PlayNextPreview()
        {
            if (previewQueue.Count == 0) { StopPreview(); return; }
            string path = previewQueue[0];
            previewQueue.RemoveAt(0);
            try
            {
                previewReader = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                    ? new WaveFileReader(path)
                    : new MediaFoundationReader(path);
                previewOut = new WaveOutEvent();
                previewOut.Init(previewReader.ToSampleProvider());
                var thisOut = previewOut;
                previewOut.PlaybackStopped += (s, e) => BeginInvoke(new Action(() =>
                {
                    if (previewOut != thisOut) return; // stopped by user
                    DisposePreviewPlayer();
                    if (previewQueue.Count > 0) PlayNextPreview(); else StopPreview();
                }));
                previewOut.Play();
                btnPreview.Text = "■ Stop";
                AppendLog($"Preview: {Path.GetFileName(path)} ({previewReader.TotalTime.TotalSeconds:0.0}s)");
            }
            catch (Exception ex)
            {
                StopPreview();
                AppendLog($"Preview failed: {ex.Message}");
            }
        }

        private void StopPreview()
        {
            previewQueue.Clear();
            DisposePreviewPlayer();
            if (btnPreview != null) btnPreview.Text = "▶ Preview";
        }

        private void DisposePreviewPlayer()
        {
            var o = previewOut; var r = previewReader;
            previewOut = null; previewReader = null;
            try { o?.Stop(); } catch { }
            o?.Dispose();
            r?.Dispose();
        }

        private string SoundsDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sounds");

        private void PopulateSoundLibrary()
        {
            comboBoxLibrary.SelectedIndexChanged -= ComboBoxLibrary_SelectedIndexChanged;
            try { FillSoundLibrary(); }
            finally { comboBoxLibrary.SelectedIndexChanged += ComboBoxLibrary_SelectedIndexChanged; }
        }

        private void FillSoundLibrary()
        {
            comboBoxLibrary.Items.Clear();
            comboBoxLibrary.Items.Add(AllTargetsName);
            comboBoxLibrary.Items.Add("Custom file…");
            if (comboBoxLibrary.Items.Count > 1)
                comboBoxLibrary.SelectedIndex = 0;
        }

        private async Task EnsureDefaultLibrarySoundAsync()
        {
            if (comboBoxLibrary.SelectedItem is not string name) return;
            if (name == "Custom file…")
            {
                if (!string.IsNullOrEmpty(lastSoundPath) && File.Exists(lastSoundPath))
                    await LoadSoundsAsync(new List<(string, string)> { (lastSoundPath, Path.GetFileName(lastSoundPath)) });
                return;
            }
            await LoadLibrarySoundAsync(name);
        }

        private async void ComboBoxLibrary_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBoxLibrary.SelectedItem is not string name || name == "Custom file…")
                return;
            await LoadLibrarySoundAsync(name);
        }

        private async Task LoadLibrarySoundAsync(string displayName)
        {
            if (displayName == AllTargetsName)
            {
                var all = soundLibrary
                    .Select(x => (Path.Combine(SoundsDirectory, x.FileName), x.Name))
                    .Where(x => File.Exists(x.Item1)).ToList();
                if (all.Count == 0) { SetStatus("Missing library files", Color.OrangeRed); return; }
                await LoadSoundsAsync(all);
                return;
            }
            var entry = soundLibrary.FirstOrDefault(s => s.Name == displayName);
            if (entry.FileName == null) return;
            string path = Path.Combine(SoundsDirectory, entry.FileName);
            if (!File.Exists(path))
            {
                SetStatus("Missing library file", Color.OrangeRed);
                return;
            }
            await FingerprintFileAsync(path, displayName);
        }

        private Task FingerprintFileAsync(string filePath, string label) =>
            LoadSoundsAsync(new List<(string, string)> { (filePath, label) });

        private async Task LoadSoundsAsync(List<(string Path, string Label)> sounds)
        {
            string names = string.Join(", ", sounds.Select(x => x.Label));
            bool isLibrary = sounds.Count > 0 && sounds.All(x => soundLibrary.Any(l =>
                string.Equals(Path.GetFullPath(Path.Combine(SoundsDirectory, l.FileName)), Path.GetFullPath(x.Path), StringComparison.OrdinalIgnoreCase)));
            string shownName = isLibrary ? AllTargetsName : "Custom";
            SetStatus($"Fingerprinting {shownName}…", ColGoldHi);
            try
            {
                foreach (var id in activeTracks.Keys.ToList())
                {
                    try { await modelService.DeleteTrackAsync(id); } catch { }
                }
                activeTracks.Clear();
                referenceTrackId = null;

                foreach (var (filePath, label) in sounds)
                {
                    string id = Guid.NewGuid().ToString();
                    var track = new TrackInfo(id, label, "library");
                    var hashes = await FingerprintCommandBuilder.Instance
                        .BuildFingerprintCommand()
                        .From(filePath)
                        .UsingServices(audioService)
                        .Hash();
                    await modelService.InsertAsync(track, hashes);
                    activeTracks[id] = label;
                    referenceTrackId ??= id;
                }

                loadedPaths = sounds.Select(x => x.Path).ToList();
                previewIndex = 0;
                lastSoundPath = sounds.Count == 1 ? sounds[0].Path : lastSoundPath;
                SetStatus($"Loaded: {shownName}", Color.LightGreen);
                AppendLog($"Listening for → {names}");
            }
            catch (Exception ex)
            {
                SetStatus("Load failed", Color.OrangeRed);
                MessageBox.Show(ex.Message);
            }
        }

        private async void BtnLoad_Click(object sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog { Filter = "Audio|*.wav;*.mp3;*.flac;*.ogg|WAV files|*.wav" };
            if (ofd.ShowDialog() != DialogResult.OK) return;

            comboBoxLibrary.SelectedIndexChanged -= ComboBoxLibrary_SelectedIndexChanged;
            int customIndex = comboBoxLibrary.Items.IndexOf("Custom file…");
            if (customIndex >= 0) comboBoxLibrary.SelectedIndex = customIndex;
            comboBoxLibrary.SelectedIndexChanged += ComboBoxLibrary_SelectedIndexChanged;

            await FingerprintFileAsync(ofd.FileName, Path.GetFileName(ofd.FileName));
        }

        private async void BtnStart_Click(object sender, EventArgs e)
        {
            if (activeTracks.Count == 0)
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
            var cts = new CancellationTokenSource();
            sessionCts = cts;
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

            gameWindow = GetForegroundWindow();
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
            sessionEndTick = chkAutoStop.Checked && nudSessionHours.Value > 0
                ? NowTick + (long)((double)nudSessionHours.Value * 3_600_000)
                : null;
            if (sessionEndTick != null)
                AppendLog($"Auto-stop at {DateTime.Now.AddHours((double)nudSessionHours.Value):HH:mm:ss} ({nudSessionHours.Value:0.##} h)");
            UpdateSessionStatus();

            await SessionLoopAsync(ct);
        }

        private void BtnStop_Click(object sender, EventArgs e)
        {
            isRunning = false;
            sessionCts?.Cancel();
            listenCts?.Cancel();
            currentBite = null;
            StopCapture();
            sessionEndTick = null;
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            SetStatus("Stopped — test detection first", ColGoldHi);
            AppendLog(catches > 0 ? $"Stopped ({catches} bites this session)" : "Stopped");
            UpdateLiveMeters(0, -120);
            UpdateSessionStatus();
        }

        private string ListeningStatus => chkSendInputs.Checked ? "Listening… inputs ON" : "Listening… detection only";

        // Whole fishing session: (buff) → cast → listen → reel in → wait → (buff) → cast → ...
        private async Task SessionLoopAsync(CancellationToken ct)
        {
            try
            {
                nextBuffTick = null;
                if (chkBuffEnabled.Checked && chkBuffOnStart.Checked)
                    await BuffSequenceAsync(ct);
                await CastAsync("Cast", ct);

                while (!ct.IsCancellationRequested)
                {
                    SetStatus(ListeningStatus, Color.LightGreen);
                    string hit = await ListenWindowAsync(ct);
                    if (hit != null)
                    {
                        catches++;
                        double react = 0.5 + rng.NextDouble() * 1.0;
                        await Task.Delay(TimeSpan.FromSeconds(react), ct);
                        string reel = await PressAsync(SelectedKey(comboBoxKey), false, ct);
                        AppendLog($"{hit} heard · {react:F3}s → reel in {reel}");

                        double min = (double)nudCastMin.Value, max = Math.Max(min, (double)nudCastMax.Value);
                        double wait = min + rng.NextDouble() * (max - min);
                        SetStatus($"Caught · casting in {wait:0.0}s", ColGoldHi);
                        await Task.Delay(TimeSpan.FromSeconds(wait), ct);
                    }
                    else
                    {
                        AppendLog($"No bite in {ListenWindowSeconds} s → recovery cast");
                    }

                    // Buffs are renewed only here, between catches.
                    if (BuffDue()) await BuffSequenceAsync(ct);
                    await CastAsync(hit != null ? "Cast" : "Recovery cast", ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppendLog($"Session error: {ex.Message}");
                if (isRunning && sessionCts?.Token == ct) BtnStop_Click(this, EventArgs.Empty);
            }
        }

        private async Task CastAsync(string label, CancellationToken ct)
        {
            SetStatus($"{label}…", ColGoldHi);
            string o = await PressAsync(SelectedKey(comboBoxKey), true, ct);
            AppendLog($"{label} → {o}");
            // short settle so the cast sound itself isn't heard as a bite
            await Task.Delay(400 + rng.Next(300), ct);
        }

        private bool BuffDue()
        {
            if (!chkBuffEnabled.Checked) { nextBuffTick = null; return false; }
            nextBuffTick ??= NowTick + (long)((double)nudBuffInterval.Value * 60_000);
            return NowTick >= nextBuffTick;
        }

        private async Task BuffSequenceAsync(CancellationToken ct)
        {
            double wait = (double)nudBuffWait.Value;
            SetStatus("Buffing…", ColGoldHi);
            string b = await PressAsync(SelectedKey(comboBoxBuffKey), true, ct);
            await Task.Delay(TimeSpan.FromSeconds(wait), ct);
            string r = await PressAsync(SelectedKey(comboBoxRodKey), true, ct);
            await Task.Delay(TimeSpan.FromSeconds(wait), ct);
            nextBuffTick = NowTick + (long)((double)nudBuffInterval.Value * 60_000);
            AppendLog($"Buff → {b}, rod → {r} · next in {nudBuffInterval.Value:0} min");
        }

        // Presses a key honoring Send inputs and the focus pause. waitForFocus: wait until the game is active again.
        private async Task<string> PressAsync(KeyOption key, bool waitForFocus, CancellationToken ct)
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
                await Task.Delay(300 + rng.Next(300), ct);
            }
            await TriggerKeyPressAsync(key.Code);
            return key.Name;
        }

        // One 40 s listen window. Returns the label of the sound heard, or null on timeout.
        private async Task<string> ListenWindowAsync(CancellationToken ct)
        {
            listenCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var bite = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            currentBite = bite;
            simThreshold = (float)nudSimilarity.Value;
            volThreshold = (float)nudMinVolume.Value;
            lastSimilarity = 0;
            realtimeSamples = new BlockingCollection<AudioSamples>();
            StartCapture();
            var timeout = Task.Delay(TimeSpan.FromSeconds(ListenWindowSeconds), listenCts.Token);
            var done = await Task.WhenAny(bite.Task, timeout);
            if (currentBite == bite) currentBite = null;
            listenCts.Cancel();
            StopCapture();
            ct.ThrowIfCancellationRequested();
            return done == bite.Task ? bite.Task.Result : null;
        }

        private void StartCapture()
        {
            StopCapture();
            var dev = SelectedDevice();
            loopback = dev != null ? new WasapiLoopbackCapture(dev) : new WasapiLoopbackCapture();

            loopback.DataAvailable += (s, a) =>
            {
                if (!isRunning || realtimeSamples.IsAddingCompleted) return;

                var samples = new float[a.BytesRecorded / 4];
                Buffer.BlockCopy(a.Buffer, 0, samples, 0, a.BytesRecorded);

                // rough peak dBFS for live meter
                float peak = 0f;
                for (int i = 0; i < samples.Length; i++)
                {
                    float v = Math.Abs(samples[i]);
                    if (v > peak) peak = v;
                }
                float db = peak <= 1e-8f ? -120f : (float)(20.0 * Math.Log10(peak));
                lastVolumeDb = db;
                BeginInvoke(new Action(() => UpdateLiveMeters(lastSimilarity, lastVolumeDb)));

                if (db < volThreshold)
                    return; // too quiet — skip fingerprinting this chunk

                realtimeSamples.Add(new AudioSamples(samples, "loopback", loopback.WaveFormat.SampleRate));
            };

            loopback.StartRecording();
            _ = Task.Run(async () => await RunRealtimeQueryAsync(listenCts.Token));
        }

        private void StopCapture()
        {
            if (loopback != null)
            {
                try { loopback.StopRecording(); } catch { }
                loopback.Dispose();
                loopback = null;
            }
            if (!realtimeSamples.IsAddingCompleted)
                realtimeSamples.CompleteAdding();
        }

        private async IAsyncEnumerable<AudioSamples> ConsumeSamplesAsync([EnumeratorCancellation] CancellationToken token)
        {
            foreach (var sample in realtimeSamples.GetConsumingEnumerable(token))
            {
                yield return sample;
                await Task.Yield();
            }
        }

        private async Task RunRealtimeQueryAsync(CancellationToken token)
        {
            try
            {
                await QueryCommandBuilder.Instance
                    .BuildRealtimeQueryCommand()
                    .From(ConsumeSamplesAsync(token))
                    .WithRealtimeQueryConfig(config =>
                    {
                        config.SuccessCallback = result =>
                        {
                            if (!isRunning || token.IsCancellationRequested)
                                return Task.CompletedTask;

                            var entries = result.ResultEntries?
                                .Where(x => x.Audio != null && activeTracks.ContainsKey(x.TrackId))
                                .OrderByDescending(x => x.Audio.Confidence)
                                .ToList();
                            var best = entries?.FirstOrDefault();
                            if (best != null)
                            {
                                NoteSimilarity((float)best.Audio.Confidence);

                                if (best.Audio.Confidence >= simThreshold)
                                {
                                    string hit = activeTracks.TryGetValue(best.TrackId, out var lbl) ? lbl : "target";
                                    currentBite?.TrySetResult(hit);
                                }
                            }

                            return Task.CompletedTask;
                        };
                        config.DidNotPassFilterCallback = result =>
                        {
                            var best = result.ResultEntries?.FirstOrDefault();
                            if (best?.Audio != null && activeTracks.ContainsKey(best.TrackId))
                                NoteSimilarity((float)best.Audio.Confidence);
                            return Task.CompletedTask;
                        };
                        return config;
                    })
                    .UsingServices(modelService)
                    .Query(token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void NoteSimilarity(float sim)
        {
            lastSimilarity = sim;
            if (sim > peakSimilarity) peakSimilarity = sim;
            try { BeginInvoke(new Action(() => UpdateLiveMeters(lastSimilarity, lastVolumeDb))); } catch { }
        }

        private bool GameIsActive()
        {
            if (gameWindow == IntPtr.Zero) return false;
            return GetForegroundWindow() == gameWindow;
        }

        private static string WindowTitle(IntPtr h)
        {
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(h, sb, sb.Capacity);
            return sb.Length > 0 ? sb.ToString() : "(untitled)";
        }

        private void UpdateFocusStatus()
        {
            if (!isRunning || !chkSendInputs.Checked || btnStart.Enabled) { focusPausedShown = false; return; }
            if (gameWindow == IntPtr.Zero && lblStatus.Text.StartsWith("Starting")) return;
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
            string text;
            if (isRunning && sessionEndTick != null)
            {
                var left = TimeSpan.FromMilliseconds(sessionEndTick.Value - NowTick);
                if (left <= TimeSpan.Zero)
                {
                    AppendLog("Session time limit reached → stopping");
                    BtnStop_Click(this, EventArgs.Empty);
                    return;
                }
                text = $"Auto-stop in {(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}";
            }
            else if (isRunning)
                text = "No session limit";
            else if (chkAutoStop != null && chkAutoStop.Checked && nudSessionHours.Value > 0)
                text = $"Auto-stop after {nudSessionHours.Value:0.##} h";
            else
                text = "";
            if (lblSession.Text != text) lblSession.Text = text;

            if (lblNextBuff != null)
            {
                string nb = !chkBuffEnabled.Checked ? "Next buff: off"
                    : !isRunning ? (chkBuffOnStart.Checked ? "Next buff: on Start" : $"Next buff: {nudBuffInterval.Value:0} min after Start")
                    : nextBuffTick == null ? "Next buff: after the next catch"
                    : nextBuffTick <= NowTick ? "Next buff: after the current catch"
                    : $"Next buff: in {(int)TimeSpan.FromMilliseconds(nextBuffTick.Value - NowTick).TotalMinutes}:{TimeSpan.FromMilliseconds(nextBuffTick.Value - NowTick).Seconds:00} (between catches)";
                if (lblNextBuff.Text != nb) lblNextBuff.Text = nb;
            }
        }

        private void InstallHotkeyHook()
        {
            if (hookHandle != IntPtr.Zero) return;
            hookProc = HookCallback;
            hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, GetModuleHandle(null), 0);
            if (hookHandle == IntPtr.Zero)
                AppendLog("Global hotkeys unavailable; F8/Esc work only while BobberBell is focused.");
        }

        private void RemoveHotkeyHook()
        {
            if (hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hookHandle);
                hookHandle = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                bool injected = (kb.flags & LLKHF_INJECTED) != 0; // ignore keys this app sends
                if (!injected)
                {
                    if (kb.vkCode == (uint)Keys.F8)
                        BeginInvoke(new Action(() => { if (isRunning) BtnStop_Click(this, EventArgs.Empty); else if (btnStart.Enabled) BtnStart_Click(this, EventArgs.Empty); }));
                    else if (kb.vkCode == (uint)Keys.Escape && isRunning)
                        BeginInvoke(new Action(() => { if (isRunning) BtnStop_Click(this, EventArgs.Empty); }));
                }
            }
            return CallNextHookEx(hookHandle, nCode, wParam, lParam);
        }

        // Keyboard: scan-code key down, hold a random 40–90 ms, key up. Mouse 4/5: XBUTTON clicks.
        private async Task TriggerKeyPressAsync(VirtualKeyCode key)
        {
            if (key == VirtualKeyCode.XBUTTON1) { inputSim.Mouse.XButtonClick(1); return; }
            if (key == VirtualKeyCode.XBUTTON2) { inputSim.Mouse.XButtonClick(2); return; }
            if (!SendScanKey(key, false))
            {
                AppendLog($"Scan-code send failed for {key}; used virtual-key fallback");
                inputSim.Keyboard.KeyPress(key);
                return;
            }
            try { await Task.Delay(40 + rng.Next(51)); }
            finally { SendScanKey(key, true); } // always released, even if the session stops mid-hold
        }

        private void UpdateLiveMeters(float similarity, float db)
        {
            int simPct = (int)Math.Max(0, Math.Min(100, similarity * 100));
            barLiveSim.Value = simPct;
            lblLiveSim.Text = similarity <= 0 ? "—" : similarity.ToString("0.00");

            // map -80..0 dBFS to 0..100
            int volPct = (int)Math.Max(0, Math.Min(100, (db + 80f) / 80f * 100f));
            barLiveVol.Value = volPct;
            lblLiveVol.Text = db <= -119f ? "— dBFS" : $"{db:0.0} dBFS";
            if (peakSimilarity > 0) lblPeak.Text = $"peak {peakSimilarity:0.00}";
        }

        private void SetStatus(string text, Color color)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => SetStatus(text, color))); return; }
            lblStatus.Text = text;
            lblStatus.ForeColor = color;
        }

        private void AppendLog(string line)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => AppendLog(line))); return; }
            line = RedactPaths(line);
            string entry = $"{DateTime.Now:HH:mm:ss}  {line}";
            listLog.Items.Insert(0, entry);
            while (listLog.Items.Count > 200) listLog.Items.RemoveAt(listLog.Items.Count - 1);
            WriteLogFile(line);
        }

        private static void WriteLogFile(string line)
        {
            try
            {
                lock (logLock)
                {
                    Directory.CreateDirectory(AppDataDir);
                    var fi = new FileInfo(logPath);
                    if (fi.Exists && fi.Length > 2_000_000)
                        File.Move(logPath, Path.Combine(AppDataDir, "log.old.txt"), true);
                    File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
                }
            }
            catch { }
        }

        private class AppSettings
        {
            public string SelectedKey { get; set; } = "1";   // fishing key
            public decimal Similarity { get; set; } = 0.40m;
            public decimal MinVolume { get; set; } = -35m;
            public string WindowName { get; set; } = DefaultWindowName;
            public string LastSoundPath { get; set; } = "";
            public string LibrarySound { get; set; } = AllTargetsName;
            public string AudioDevice { get; set; } = "";
            public decimal CastWaitMin { get; set; } = 1.5m;
            public decimal CastWaitMax { get; set; } = 2.5m;
            public bool AutoStopEnabled { get; set; } = true;
            public decimal AutoStopHours { get; set; } = 0.25m;
            public bool BuffEnabled { get; set; } = false;
            public decimal BuffIntervalMinutes { get; set; } = 10m;
            public string BuffKey { get; set; } = "2";
            public string RodKey { get; set; } = "3";
            public decimal BuffWaitSeconds { get; set; } = 3m;
            public bool BuffOnStart { get; set; } = false;
            // SendInputs is intentionally not persisted: it always starts off.
        }

        private static decimal Clamp(decimal v, NumericUpDown n) => Math.Clamp(v, n.Minimum, n.Maximum);

        private void BtnSave_Click(object sender, EventArgs e)
        {
            var settings = new AppSettings
            {
                SelectedKey = SelectedKey(comboBoxKey)?.Name ?? "1",
                Similarity = nudSimilarity.Value,
                MinVolume = nudMinVolume.Value,
                WindowName = txtWindowName.Text.Trim(),
                LastSoundPath = lastSoundPath ?? "",
                LibrarySound = comboBoxLibrary.SelectedItem as string ?? AllTargetsName,
                AudioDevice = SelectedDeviceName(),
                CastWaitMin = nudCastMin.Value,
                CastWaitMax = nudCastMax.Value,
                AutoStopEnabled = chkAutoStop.Checked,
                AutoStopHours = nudSessionHours.Value,
                BuffEnabled = chkBuffEnabled.Checked,
                BuffIntervalMinutes = nudBuffInterval.Value,
                BuffKey = SelectedKey(comboBoxBuffKey)?.Name ?? "2",
                RodKey = SelectedKey(comboBoxRodKey)?.Name ?? "3",
                BuffWaitSeconds = nudBuffWait.Value,
                BuffOnStart = chkBuffOnStart.Checked
            };
            try
            {
                Directory.CreateDirectory(AppDataDir);
                string json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                string tmp = settingsPath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(settingsPath)) File.Replace(tmp, settingsPath, null);
                else File.Move(tmp, settingsPath);
                AppendLog($"Settings saved → {settingsPath}");
                SetStatus("Settings saved", Color.LightGreen);
            }
            catch (Exception ex)
            {
                AppendLog($"Save failed: {ex.Message}");
                SetStatus("Save failed", Color.OrangeRed);
            }
        }

        private void LoadSettingsOnStartup(bool showMessage = false)
        {
            try
            {
                if (!File.Exists(settingsPath) && File.Exists(legacySettingsPath))
                {
                    Directory.CreateDirectory(AppDataDir);
                    File.Copy(legacySettingsPath, settingsPath);
                    AppendLog($"Settings migrated from {legacySettingsPath}");
                }
            }
            catch (Exception ex) { AppendLog($"Settings migration failed: {ex.Message}"); }

            if (!File.Exists(settingsPath))
            {
                if (showMessage) AppendLog("No saved settings; using defaults");
                return;
            }
            try
            {
                var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath));
                if (settings == null) return;

                if (!SelectKey(comboBoxKey, settings.SelectedKey)) SelectKey(comboBoxKey, "1");
                if (!SelectKey(comboBoxBuffKey, settings.BuffKey)) SelectKey(comboBoxBuffKey, "2");
                if (!SelectKey(comboBoxRodKey, settings.RodKey)) SelectKey(comboBoxRodKey, "3");
                nudSimilarity.Value = Clamp(settings.Similarity, nudSimilarity);
                nudMinVolume.Value = Clamp(settings.MinVolume, nudMinVolume);
                decimal cmin = Clamp(settings.CastWaitMin, nudCastMin), cmax = Clamp(settings.CastWaitMax, nudCastMax);
                if (cmax < cmin) cmax = cmin;
                nudCastMax.Value = cmax;
                nudCastMin.Value = cmin;
                // A hand-edited settings.json can't silently disable the limit: >168 caps at 168, negative goes back to 0.25.
                decimal hours = settings.AutoStopHours;
                if (hours > MaxSessionHours) { AppendLog($"Saved Stop after {hours:0.##} h is above {MaxSessionHours:0} h; capped at {MaxSessionHours:0} h"); hours = MaxSessionHours; }
                else if (hours < 0m) { AppendLog($"Saved Stop after {hours:0.##} h is invalid; using {DefaultSessionHours:0.##} h"); hours = DefaultSessionHours; }
                nudSessionHours.Value = hours;
                chkAutoStop.Checked = settings.AutoStopEnabled;
                nudBuffInterval.Value = Clamp(Math.Round(settings.BuffIntervalMinutes), nudBuffInterval);
                nudBuffWait.Value = Clamp(settings.BuffWaitSeconds, nudBuffWait);
                chkBuffEnabled.Checked = settings.BuffEnabled;
                chkBuffOnStart.Checked = settings.BuffOnStart;

                // Old default names ("WeatherBug - v1.x", "BobberBell - v1.0", ...) follow the current default.
                bool oldDefault = settings.WindowName != null &&
                    System.Text.RegularExpressions.Regex.IsMatch(settings.WindowName.Trim(), @"^(WeatherBug|BobberBell)( - v\d+(\.\d+)*)?$");
                string savedName = string.IsNullOrWhiteSpace(settings.WindowName) || oldDefault
                    ? DefaultWindowName
                    : settings.WindowName.Trim();
                if (savedName.Length > 128) savedName = savedName.Substring(0, 128);
                txtWindowName.Text = savedName;
                Text = savedName;
                lastSoundPath = settings.LastSoundPath ?? "";
                if (lastSoundPath.Length > 0 && !File.Exists(lastSoundPath))
                {
                    AppendLog($"Last sound file not found, cleared: {lastSoundPath}");
                    lastSoundPath = "";
                }
                chkSendInputs.Checked = false; // never restored

                if (!string.IsNullOrWhiteSpace(settings.AudioDevice))
                {
                    int idx = renderDevices.FindIndex(d => string.Equals(d.FriendlyName, settings.AudioDevice, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) comboBoxDevice.SelectedIndex = idx;
                    else
                    {
                        startupWarning = $"Saved audio output \"{settings.AudioDevice}\" was not found.\nUsing {comboBoxDevice.Text} instead. Pick the right output on the Audio tab and Save settings.";
                        AppendLog($"Warning: saved audio output \"{settings.AudioDevice}\" not found; using {comboBoxDevice.Text}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(settings.LibrarySound) &&
                    comboBoxLibrary.Items.Contains(settings.LibrarySound))
                {
                    comboBoxLibrary.SelectedIndexChanged -= ComboBoxLibrary_SelectedIndexChanged;
                    comboBoxLibrary.SelectedItem = settings.LibrarySound;
                    comboBoxLibrary.SelectedIndexChanged += ComboBoxLibrary_SelectedIndexChanged;
                }

                if (showMessage) AppendLog($"Settings loaded from {settingsPath}");
            }
            catch (Exception ex) { AppendLog($"Settings load failed: {ex.Message}"); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            isRunning = false;
            sessionCts?.Cancel();
            listenCts?.Cancel();
            StopCapture();
            focusTimer?.Stop();
            RemoveHotkeyHook();
            StopPreview();
            base.OnFormClosing(e);
        }
    }

    internal class ModernButton : Button
    {
        private readonly bool filled;
        private readonly Color accent, accentHi, surface, text;
        private bool hover, down;

        public ModernButton(bool filled, Color accent, Color accentHi, Color surface, Color text)
        {
            this.filled = filled; this.accent = accent; this.accentHi = accentHi; this.surface = surface; this.text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
        }

        protected override bool ShowFocusCues => false;
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            // Paint whatever is behind (card fill + glass band) so the rounded corners blend in.
            if (Parent != null) base.OnPaintBackground(e); else g.Clear(Form1.SurfaceOf(Parent));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using var path = Form1.RoundedRect(r, 6);
            Color fg;
            if (filled)
            {
                Color top = !Enabled ? Color.FromArgb(70, 62, 44) : down ? accent : hover ? accentHi : Color.FromArgb(214, 178, 108);
                Color bottom = !Enabled ? Color.FromArgb(58, 52, 38) : down ? Color.FromArgb(170, 134, 70) : accent;
                using var br = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), top, bottom, LinearGradientMode.Vertical);
                g.FillPath(br, path);
                fg = Enabled ? text : Color.FromArgb(120, 110, 90);
            }
            else
            {
                Color fill = !Enabled ? surface : down ? Color.FromArgb(40, accent) : hover ? Color.FromArgb(26, accent) : surface;
                using (var br = new SolidBrush(fill)) g.FillPath(br, path);
                Color border = !Enabled ? Color.FromArgb(60, accent) : hover ? accentHi : Color.FromArgb(170, accent);
                using var pen = new Pen(border, 1f);
                g.DrawPath(pen, path);
                fg = Enabled ? text : Color.FromArgb(90, text);
            }
            if (Enabled) PaintSheen(g, path, r);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        // Glass/metal sheen, clipped to the rounded shape: white gradient over the top ~45%,
        // a soft radial glint near the top-left (gold buttons), and a 1 px light inner top edge.
        private void PaintSheen(Graphics g, GraphicsPath path, RectangleF r)
        {
            var state = g.Save();
            g.SetClip(path, CombineMode.Intersect);
            int a = filled ? (down ? 22 : hover ? 75 : 55) : (down ? 8 : hover ? 30 : 20);
            var top = new RectangleF(r.X, r.Y, r.Width, Math.Max(2f, r.Height * 0.45f));
            using (var sheen = new LinearGradientBrush(new RectangleF(top.X, top.Y - 1, top.Width, top.Height + 2),
                       Color.FromArgb(a, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.Vertical))
                g.FillRectangle(sheen, top);
            if (filled && !down)
            {
                using var glint = new GraphicsPath();
                glint.AddEllipse(r.X - r.Width * 0.15f, r.Y - r.Height * 0.6f, r.Width * 0.7f, r.Height * 1.3f);
                using var pg = new PathGradientBrush(glint)
                {
                    CenterPoint = new PointF(r.X + r.Width * 0.18f, r.Y + r.Height * 0.12f),
                    CenterColor = Color.FromArgb(hover ? 50 : 34, Color.White),
                    SurroundColors = new[] { Color.FromArgb(0, Color.White) }
                };
                g.FillPath(pg, glint);
            }
            using (var edge = new Pen(Color.FromArgb(down ? 15 : filled ? 40 : 26, Color.White), 1f))
                g.DrawLine(edge, r.X + 6, r.Y + 1, r.Right - 6, r.Y + 1);
            g.Restore(state);
        }
    }

    // Tab strip button: the gold underline marks the active tab, so no system focus rectangle.
    internal class TabButton : Button
    {
        protected override bool ShowFocusCues => false;
    }

    internal class MeterBar : Control
    {
        private readonly Color track, fillA, fillB;
        private int value;
        // Runtime-only properties (never designer-serialized); required by the .NET 10 WinForms analyzer (WFO1000).
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Minimum { get; set; } = 0;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Maximum { get; set; } = 100;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Value
        {
            get => value;
            set { int v = Math.Max(Minimum, Math.Min(Maximum, value)); if (v != this.value) { this.value = v; Invalidate(); } }
        }

        public MeterBar(Color track, Color fillA, Color fillB)
        {
            this.track = track; this.fillA = fillA; this.fillB = fillB;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (Parent != null) base.OnPaintBackground(e); else g.Clear(Form1.SurfaceOf(Parent)); // keeps the card's glass band behind the bar
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float rad = Height / 2f;
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            // Inset glass track: a touch darker than the card plus a faint rim, so it reads on top of the glass band.
            using (var path = Form1.RoundedRect(r, rad))
            using (var br = new SolidBrush(Color.FromArgb(track.R / 3, track.G / 2, track.B / 2)))
            using (var rim = new Pen(Color.FromArgb(70, 52, 82, 124)))
            {
                g.FillPath(br, path);
                g.DrawPath(rim, path);
            }
            float frac = Maximum > Minimum ? (value - Minimum) / (float)(Maximum - Minimum) : 0;
            float w = (Width - 1) * frac;
            if (w >= 2)
            {
                var fr = new RectangleF(0, 0, Math.Max(w, Height), Height - 1);
                using var path = Form1.RoundedRect(fr, rad);
                using var br = new LinearGradientBrush(new RectangleF(0, 0, Width, Height), fillA, fillB, LinearGradientMode.Horizontal);
                g.FillPath(br, path);
                using var sheen = new LinearGradientBrush(new RectangleF(0, -1, Width, Height / 2f + 2),
                    Color.FromArgb(70, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.Vertical);
                var state = g.Save(); g.SetClip(path, CombineMode.Intersect);
                g.FillRectangle(sheen, 0, 0, Width, Height / 2f);
                g.Restore(state);
            }
        }
    }

    internal class ToggleSwitch : CheckBox
    {
        private readonly Color off, on, border, textColor;
        public ToggleSwitch(Color off, Color on, Color border, Color textColor)
        {
            this.off = off; this.on = on; this.border = border; this.textColor = textColor;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = false;
            Size = new Size(320, 24);
            Cursor = Cursors.Hand;
        }
        protected override bool ShowFocusCues => false;
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Form1.SurfaceOf(Parent));
            var pill = new RectangleF(1, (Height - 18) / 2f, 34, 18);
            using (var path = Form1.RoundedRect(pill, 9))
            {
                using (var br = new SolidBrush(Checked ? on : off)) g.FillPath(br, path);
                using var pen = new Pen(Checked ? on : border, 1f);
                g.DrawPath(pen, path);
            }
            float kx = Checked ? pill.Right - 16 : pill.X + 2;
            using (var knob = new SolidBrush(Checked ? Color.FromArgb(20, 16, 8) : Color.FromArgb(150, 160, 180)))
                g.FillEllipse(knob, kx, pill.Y + 2, 14, 14);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(44, 0, Width - 44, Height), textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
        }
    }
}
