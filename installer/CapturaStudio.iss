; Captura Studio - instalador Windows (Inno Setup 6)
; Instala por utilizador em %LOCALAPPDATA%\CapturaStudio, sem admin.
; O payload e gerado por build-installer.ps1 (bundle extraido do ZIP ou build-bundle.ps1).

#define MyAppName "Captura Studio"
#ifndef MyAppVersion
  #define MyAppVersion "1.1.0"
#endif
#ifndef MyAppVersionInfo
  #define MyAppVersionInfo "1.1.0.0"
#endif
#define MyAppPublisher "OliveTree"
#define MyAppExeName "CapturaStudio.exe"

[Setup]
AppId={{9F4C2B7E-3A6D-4E8B-9C1F-7D2A5B8E6C11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\CapturaStudio
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=out
OutputBaseFilename=CapturaStudio-{#MyAppVersion}-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\app\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
DirExistsWarning=no
SetupLogging=yes
SetupIconFile=..\uwc03-winui\Assets\videotape.ico
VersionInfoVersion={#MyAppVersionInfo}

[Languages]
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "bundle\app\*"; DestDir: "{app}\app"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "bundle\captura-rec.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "bundle\ffmpeg\ffmpeg.exe"; DestDir: "{app}\ffmpeg"; Flags: ignoreversion
Source: "bundle\ffmpeg\ffprobe.exe"; DestDir: "{app}\ffmpeg"; Flags: ignoreversion
Source: "bundle\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "bundle\LICENSE-FFMPEG-GPLv3.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\app\{#MyAppExeName}"; WorkingDir: "{app}\app"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\app\{#MyAppExeName}"; WorkingDir: "{app}\app"; Tasks: desktopicon

[Run]
Filename: "{app}\app\{#MyAppExeName}"; WorkingDir: "{app}\app"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  StopResult: Integer;

procedure StopCaptura;
begin
  Exec('taskkill.exe', '/F /IM CapturaStudio.exe /T', '', SW_HIDE, ewWaitUntilTerminated, StopResult);
  Exec('taskkill.exe', '/F /IM captura-rec.exe /T', '', SW_HIDE, ewWaitUntilTerminated, StopResult);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopCaptura;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    StopCaptura;
end;
