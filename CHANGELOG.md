# Changelog

All notable changes to BobberBell are documented here.

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

[1.0.1]: https://github.com/Moosecat313/BobberBell/releases/tag/v1.0.1
[1.0.0]: https://github.com/Moosecat313/BobberBell/releases/tag/v1.0.0
