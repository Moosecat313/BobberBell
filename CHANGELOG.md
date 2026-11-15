# Changelog

All notable changes to BobberBell are documented here.

## [1.0.5] - 2026-11-15

### Fixed
- Dropdown lists (for example Detection mode) opened one row tall, so *Volume trigger* could only be picked with the arrow keys. The lists now open tall enough to show all their entries.

## [1.0.4] - 2026-11-15

### Added
- **Volume trigger detection mode** (Audio tab, default stays Fingerprint). After an ignore time following each cast (2.5 s), a sound at or above the trigger level (-38 dBFS) for at least 0.6 s whose spectral centroid is 300-3200 Hz counts as a bite. For servers whose splash never matches the reference sounds. Rejected sounds are logged with length, level and tone. Uses the same reel-in, recovery cast, auto-stop and focus-pause flow.
- `VolumeTriggerDetector` in `BobberBell.Core` with unit tests, plus a regression test against a private-server capture (run locally; the game audio is not committed).

Existing settings files load unchanged (new keys default to Fingerprint mode). Licensing, trial and anti-tamper code are unchanged.

## [1.0.3] - 2026-11-15

### Fixed
- Cancelling the license key prompt (or pressing OK with nothing typed) no longer shows "Invalid license key." It counts as a cancel: at startup you go back to the trial choice while trial time is left, otherwise the app closes quietly; from the trial footer or the License button the prompt just closes; when the trial has ended the app closes. A wrong key still shows the error.
- When BobberBell closes at startup (no key, a wrong key, or the start-up dialog cancelled), the reason, including why no trial was offered (for example an edited trial record), is now written to `log.txt` first.
- The startup license and trial dialogs open centered, top-most (in front of a fullscreen game) and with a taskbar button. The trial choice is now a regular dialog instead of a TaskDialog, because Windows won't raise a TaskDialog from a background launch above a fullscreen game. Prompts shown while the app is running don't force themselves in front.

### Added
- `KeyPromptFlow` in `BobberBell.Core` (cancel detection and what happens after the key prompt) with unit tests.

Licensing (`LicenseService`, `license.dat` format, PBKDF2 hashes, binding literal), `ProtectionCheck` and its blocklist are unchanged.

## [1.0.2] - 2026-11-15

### Added
- **5-minute trial per PC.** Without a stored license, BobberBell offers *Enter license key* or *Start / Continue trial (M:SS left)*. The trial counts total running time on this PC (monotonic clock), shows the time left at the bottom of the window, saves used time every 15 s and on close, and at 0:00 stops detection and key sending and asks for a key (a valid key continues licensed; otherwise the app closes). With no trial time left, start-up asks for a key exactly as before.
- Trial state is stored as signed used-seconds (HMAC-SHA256 keyed from the PC's MachineGuid) in `%LOCALAPPDATA%\BobberBell\trial.dat` and mirrored in `HKCU\Software\BobberBell`; the larger valid copy wins and used time never decreases. An edited record, or one copied from another PC, ends the trial. If the MachineGuid can't be read there is no trial (a key is required).
- `TrialService` in `BobberBell.Core` with 21 unit tests.

### Security
- `trial.dat` added to `.gitignore` and to the CI packaging guard. Licensing (`license.dat` format, PBKDF2 hashes, binding literal), `ProtectionCheck` and its blocklist are unchanged.

## [1.0.1] - 2026-11-15
Bug-fix release, plus the engineering and security work from the portfolio branch.

### Fixed
- **Fixed: splash detection never fired because loopback audio wasn't converted to 5512 Hz mono.** WASAPI loopback delivers the device mix format (typically 48 kHz, 32-bit float, stereo), but SoundFingerprinting's realtime query only accepts 5512 Hz mono, so the first loud chunk threw `Provided samples are not sampled in the required sample rate 5512`, the query died and no bite was ever detected. Capture audio is now decoded (float or 8/16/24/32-bit PCM, any channel count), averaged to mono and resampled to 5512 Hz with a band-limited streaming resampler (windowed sinc, anti-aliased, state kept across chunks). The volume meter still reads the original audio.
- A match is now reported once 1 s of a reference has been heard. The library's default realtime filter needs 5 s of match and waits for the match to end, which a ~3 s splash never satisfied.
- Chunks below the volume gate are fingerprinted as silence instead of being dropped, so the query's timeline keeps moving.
- If the fingerprint query ever fails, the error is logged once and the query restarts for the rest of the listen window instead of dying silently.
- Reference sounds (library and custom) are fingerprinted from the same 5512 Hz mono stream (decoded by `NAudioService`), now shared through `SplashFingerprintEngine` in `BobberBell.Core`.

### Added
- Tests for the audio conversion (output rate and length, mono downmix, tone frequency and level preserved, aliasing rejected, PCM formats) and the live pipeline (each splash detected through simulated 48 kHz stereo loopback; tone, quiet splash and query-failure restart cases).
- `tools/BobberBell.DetectionHarness`: a console tool that plays each splash through a real output device while capturing it with the app's detection core, and reports confidence per file plus non-splash controls.

### Changed
- **Architecture:** split the 2,600-line `Form1.cs` into a platform-neutral `BobberBell.Core` library (settings, licensing, session timing, detection thresholds, key catalog, file validation, logging) and a thin WinForms app (`MainForm` partials, controls in their own files, Windows services behind `IAudioDetector`, `IInputSender`, `IMachineIdProvider`). All Win32 calls moved to `Interop/NativeMethods.cs`.
- **Logging:** one `IAppLogger` for everything (same file format, rotation and path redaction). A failed sound load now logs the error and shows `Load failed` instead of a bare error box.
- **Audio:** the realtime fingerprint query is tracked and awaited on Stop; capture, queue and query are cancelled and disposed together.

### Security
- Nullable reference types, .NET analyzers (`latest`, all security rules) and warnings-as-errors for every project.
- `settings.json`: 64 KB limit, strict source-generated JSON contract (no comments, trailing commas, duplicate keys; depth ≤ 8), and full value validation in `SettingsValidator`.
- Sound files: extension allow-list, 50 MB limit, no UNC/device paths or alternate data streams, library files confined to `Sounds\`.
- `license.dat` reads capped at 4 KB. Licensing algorithm, file format and machine binding unchanged, so existing licenses stay valid.
- P/Invoke restricted to System32; explorer is started by full path.
- Random timing jitter uses the OS CSPRNG.
- `SECURITY.md` and a STRIDE threat model in `docs/THREAT_MODEL.md`.

### Added
- xUnit v3 tests (settings validation and clamping, session timer and buffs, detection thresholds, licensing, path redaction, key catalog, file validation, settings store, an end-to-end fingerprint match).
- GitHub Actions CI on `windows-latest` (gitleaks, locked restore, build, test, vulnerable-package gate, publish, Inno Setup installer, checksums) and CodeQL (`security-extended`). Dependabot for NuGet and Actions.
- Self-contained single-file `win-x64` publish profile, Inno Setup per-user installer, assembly company/copyright info, deterministic builds and NuGet lock files. Code signing documented and ready to enable (`docs/RELEASING.md`).

## [1.0.0] - 2026-11-14
First release.

### Added
- **Sound detection:** WASAPI loopback capture with audio fingerprinting (SoundFingerprinting), plus minimum-similarity and minimum-volume thresholds, live meters and a peak readout for calibration.
- **Library sounds:** the built-in **BobberSplash** set (three splash recordings; any one counts as a bite), or a custom WAV/MP3/FLAC/OGG file. Preview plays one clip per press, and the status line shows `Loaded: BobberSplash` or `Loaded: Custom`.
- **Bite cycle:** randomized reaction and cast-wait timing, plus a recovery cast after 40 s with no bite.
- **Auto-stop session:** **on by default**, 0.25 h by default, capped at 168 h. 0 means no limit and shows a one-time warning. Hand-edited settings are clamped too.
- **Default keys:** fishing **1**, buff **2**, rod **3**. Mouse 4/5, 0–9, `-` and `=` are also available.
- **Buff and rod handling:** timed buff refresh (buff key, wait, rod key, wait, cast) that never interrupts a bite, with an optional buff on Start.
- **Safety:** Send inputs is off at every launch, inputs pause when the game isn't the active window, and F8/Esc work as global hotkeys.
- **Window tab:** custom or random window name.
- **Log tab:** activity log with a log file. Paths are shown as `%LOCALAPPDATA%` and `%APPDATA%`, never with your user name.
- **UI:** navy and gold interface with a subtle glass/metallic finish (button sheen, glass cards, metallic tab underline) and a built-in How to use guide.
- **License gating:** codes are stored only as salted PBKDF2 hashes, the license is bound to the PC, and short codes are rejected.
- **Migration:** settings, log and license move automatically from the pre-release name (WeatherBug).

### Technical
- .NET 10 (`net10.0-windows`), WinForms.
- NAudio.Core, NAudio.Wasapi and NAudio.WinMM 2.2.1; SoundFingerprinting 17.4.0 with SoundFingerprinting.Audio.NAudio 8.0.0; H.InputSimulator 1.5.0.

[1.0.5]: https://github.com/Moosecat313/BobberBell/releases/tag/v1.0.5
[1.0.4]: https://github.com/Moosecat313/BobberBell/releases/tag/v1.0.4
[1.0.3]: https://github.com/Moosecat313/BobberBell/releases/tag/v1.0.3
[1.0.2]: https://github.com/Moosecat313/BobberBell/releases/tag/v1.0.2
[1.0.1]: https://github.com/Moosecat313/BobberBell/releases/tag/v1.0.1
[1.0.0]: https://github.com/Moosecat313/BobberBell/releases/tag/v1.0.0
