# Releasing

1. Update the version in `Directory.Build.props` (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`),
   `SettingsLimits.AppVersion` in `src/BobberBell.Core/Settings/SettingsLimits.cs`, `AppVersion` in `installer/BobberBell.iss`
   and `CHANGELOG.md`.
2. Push to `main` and wait for **CI** and **CodeQL** to pass.
3. Download the `BobberBell-win-x64` artifact from the CI run. It contains:
   - `BobberBell-<version>.zip` – framework-dependent build (needs the .NET 10 Desktop Runtime), the classic release layout,
   - `BobberBell-<version>-win-x64-selfcontained.zip` – self-contained single-file exe plus `Assets\` and `Sounds\`,
   - `installer/BobberBell-<version>-setup.exe` – per-user Inno Setup installer,
   - `SHA256SUMS.txt`.
   CI fails the build if any zip contains `FriendCodes.txt`, `license.dat`, `trial.dat`, `settings.json`, a log file or a `.pdb`.
4. Optionally run `tools/BobberBell.DetectionHarness` on a Windows PC with speakers or a headset
   (`BobberBell.DetectionHarness.exe "<output device name>"`) to confirm live detection of every bundled splash.
5. Create the tag and GitHub release, attach the files and paste the SHA-256 sums into the release notes.

## Local build
```powershell
dotnet restore BobberBell.slnx --locked-mode
dotnet test --solution BobberBell.slnx -c Release
dotnet publish src\BobberBell -c Release -p:PublishProfile=win-x64-singlefile
iscc installer\BobberBell.iss
```

## Code signing (prepared, off by default)
Signing removes the SmartScreen "unknown publisher" warning and lets users verify the publisher. It needs an Authenticode
code-signing certificate, which costs money, so it is **not enabled**. Everything is in place to turn it on later:

**CI.** `ci.yml` has a *Sign exe (optional)* step that runs only when both repository secrets exist:

| Secret | Value |
|---|---|
| `SIGN_CERT_PFX_BASE64` | The `.pfx` file, base64-encoded (`[Convert]::ToBase64String([IO.File]::ReadAllBytes('cert.pfx'))`) |
| `SIGN_CERT_PASSWORD` | The `.pfx` password |

The step writes the certificate to the runner's temp folder, signs `BobberBell.exe` with SHA-256 and an RFC 3161 timestamp,
and deletes the file. Add a matching `signtool` call after the installer step (or use Inno Setup's `SignTool`, below) to sign
the installer too. For cloud HSM certificates (for example Azure Trusted Signing), replace the step with the provider's action.

**Installer.** `installer/BobberBell.iss` signs the setup and uninstaller when compiled with `/DSign` and a sign tool named
`signtool`:
```powershell
iscc "/Ssigntool=signtool.exe sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f `$qcert.pfx`$q /p `$qPASSWORD`$q `$f" /DSign installer\BobberBell.iss
```

**Verify** a signed file with `signtool verify /pa /v BobberBell.exe` or `Get-AuthenticodeSignature .\BobberBell.exe`.
