#define MyAppName "StageManager"
#ifndef MyAppVersion
#define MyAppVersion "0.1.2"
#endif
#define MyAppExeName "StageManager.exe"

[Setup]
AppId={{7F1E81D5-36ED-43A4-B105-3B4F1A572631}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={localappdata}\Programs\StageManager
DefaultGroupName={#MyAppName}
OutputDir=..\dist
OutputBaseFilename=StageManagerSetup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "..\publish\StageManager-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "autostart"; Description: "Start StageManager with Windows"; GroupDescription: "Startup options:"; Flags: unchecked

[Icons]
Name: "{group}\StageManager"; Filename: "{app}\{#MyAppExeName}"
Name: "{autostartup}\StageManager"; Filename: "{app}\{#MyAppExeName}"; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch StageManager"; Flags: nowait postinstall skipifsilent
