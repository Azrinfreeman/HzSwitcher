; Builds HzSwitcher-Setup-<version>.exe (next to this file) from ..\<version>\HzSwitcher.exe.
; Compile with: ISCC.exe /DAppVersion=1.5.0 HzSwitcher.iss
#define AppName "HzSwitcher"
; build.ps1 passes /DAppVersion=<version from the csproj>
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppExe "HzSwitcher.exe"

[Setup]
AppId={{6E1B7C52-4D0A-4B8E-9F3A-7A2C5D1E8B90}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppName}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=.
OutputBaseFilename=HzSwitcher-Setup-{#AppVersion}
SetupIconFile=..\..\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
Name: "startup"; Description: "Start HzSwitcher with Windows (minimized to tray)"; Flags: unchecked

[Files]
Source: "..\1.5.0\HzSwitcher.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "HzSwitcher"; ValueData: """{app}\{#AppExe}"" --minimized"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "KillHzSwitcher"
