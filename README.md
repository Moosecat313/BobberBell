# BobberBell 1.0

Listens for a specific sound, then pushes a specified button, hands-free.

BobberBell is a small Windows desktop app (C# / WinForms, .NET 10) built for World of Warcraft fishing. It listens to your
game's audio output (WASAPI loopback), recognises the bobber-splash sound with audio fingerprinting, then presses your
fishing key to reel in and casts again.

## Features
- **Audio:** built-in BobberSplash sounds or your own recording (WAV/MP3/FLAC/OGG), with similarity and minimum-volume thresholds and a live meter.
- **Fishing:** fishing key (default `1`), random reaction and cast-wait timing, and an auto-stop session timer (default 0.25 h, max 168 h).
- **Buff:** optional timed buff refresh with buff key (default `2`) and rod key (default `3`).
- **Window:** custom or random window title.
- **Log:** an activity log, also saved to `%LOCALAPPDATA%\BobberBell\log.txt`.
- **Hotkeys:** F8 starts and stops; Esc stops. Inputs pause while the game window isn't active.

## Requirements
- Windows 10/11 (x64)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) to run; the .NET 10 SDK (or newer) to build
- A license key (BobberBell asks for it on first launch)

## Build
```
dotnet build BobberBell.csproj -c Release
dotnet publish BobberBell.csproj -c Release -o publish\BobberBell-1.0.0
```

## Libraries
NAudio (Core, Wasapi, WinMM), SoundFingerprinting and SoundFingerprinting.Audio.NAudio, H.InputSimulator.

## Note
Automating game input may break your game's terms of service. Use at your own risk.
