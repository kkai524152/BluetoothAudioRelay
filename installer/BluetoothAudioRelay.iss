#define MyAppName "蓝牙音频中继"
#ifndef MyAppVersion
#define MyAppVersion "0.5.3"
#endif
#define MyAppPublisher "BluetoothAudioRelay"
#define MyAppExeName "BluetoothAudioRelay.exe"

[Setup]
AppId={{8F10E30B-8545-4E25-B672-B3BE6D8DD95E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\BluetoothAudioRelay
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=BluetoothAudioRelay-Setup-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ChangesAssociations=yes
SetupIconFile=..\Assets\BluetoothAudioRelay.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoDescription=蓝牙音频中继安装程序

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "..\publish\lightweight\BluetoothAudioRelay.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动{#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  if FileExists(ExpandConstant('{app}\{#MyAppExeName}')) then
  begin
    { Use a non-blocking launch so an older version's modal single-instance
      message cannot stall an upgrade before the compatibility fallback. }
    Exec(ExpandConstant('{app}\{#MyAppExeName}'), '--shutdown', '', SW_HIDE, ewNoWait, ResultCode);
    Sleep(1200);
  end;

  { Compatibility fallback when upgrading a version older than 0.5.0. }
  Exec(ExpandConstant('{cmd}'), '/C taskkill /IM {#MyAppExeName} /F >nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
