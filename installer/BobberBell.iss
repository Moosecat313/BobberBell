; Inno Setup 6 script for BobberBell.
; Build:  iscc installer\BobberBell.iss            (after: dotnet publish src\BobberBell -c Release -p:PublishProfile=win-x64-singlefile)
; Output: artifacts\installer\BobberBell-<version>-setup.exe
;
; Per-user install (no admin rights, no UAC prompt): files go to %LOCALAPPDATA%\Programs\BobberBell.
; User data (%LOCALAPPDATA%\BobberBell, %APPDATA%\BobberBell\license.dat) is never touched by install or uninstall.
;
; Code signing (off by default; certificates cost money). To enable later:
;   1. In the Inno Setup IDE or on the command line, define a sign tool, e.g.
;        iscc "/Ssigntool=signtool.exe sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f $qcert.pfx$q /p $qPASSWORD$q $f" /DSign installer\BobberBell.iss
;   2. The SignTool line below is only active when /DSign is passed. See docs/RELEASING.md.

#define AppName "BobberBell"
#ifndef AppVersion
  #define AppVersion "1.0.1"
#endif
#define AppPublisher "Chris Monoxide"
#define AppExeName "BobberBell.exe"
#define PublishDir "..\artifacts\publish\win-x64"

[Setup]
AppId={{8CB717BC-2C9C-40EB-99E3-6069C32E1C20}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright=Copyright (C) 2026 {#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\artifacts\installer
OutputBaseFilename={#AppName}-{#AppVersion}-setup
SetupIconFile=..\src\BobberBell\Assets\BobberBell.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
#ifdef Sign
SignTool=signtool
SignedUninstaller=yes
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\Sounds\*.wav"; DestDir: "{app}\Sounds"; Flags: ignoreversion
Source: "{#PublishDir}\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
