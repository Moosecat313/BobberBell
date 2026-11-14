# Changelog

All notable changes to BobberBell are documented here.

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

[1.0.0]: https://github.com/Moosecat313/BobberBell/releases/tag/v1.0.0
