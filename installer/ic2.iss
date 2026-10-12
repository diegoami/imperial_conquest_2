; Imperial Conquest 2 - Inno Setup 6 script (T131).
;
; Builds ImperialConquest2-v<AppVersion>-windows-x64-setup.exe from the exported package folder
; (rendered/export/ic2/: game\, data\, assets\). Driven by scripts/release-assets.ps1:
;   ISCC /DAppVersion=0.4.1 /DSourceDir=<ic2 folder> /DOutputDir=<dir> installer/ic2.iss
;
; THE AppId BELOW IS FIXED AND MUST NEVER CHANGE. Inno Setup identifies an installed copy by its
; AppId: a newer Setup with the same AppId upgrades that install in place, and the uninstaller
; entry stays one. A different AppId makes the next Setup a second, separate install beside the
; first, which a player would then have to remove by hand.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\rendered\export\ic2"
#endif
#ifndef OutputDir
  #define OutputDir "..\rendered\release"
#endif

#define AppName "Imperial Conquest 2"
#define AppExe "IC2.MapViewer.exe"

[Setup]
AppId={{5B7E2C1A-6F3D-4E8B-9A41-1C2D3E4F5A6B}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Imperial Conquest 2
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\Imperial Conquest 2
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=ImperialConquest2-v{#AppVersion}-windows-x64-setup
UninstallDisplayIcon={app}\game\{#AppExe}
CloseApplications=yes
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

; An upgrade carries no stale file: the three trees are removed before the new ones are copied.
; The player's saves live in the user profile, not under {app}, so they are untouched.
[InstallDelete]
Type: filesandordirs; Name: "{app}\game"
Type: filesandordirs; Name: "{app}\data"
Type: filesandordirs; Name: "{app}\assets"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\game\{#AppExe}"; WorkingDir: "{app}\game"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\game\{#AppExe}"; WorkingDir: "{app}\game"; Tasks: desktopicon

[Run]
Filename: "{app}\game\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; WorkingDir: "{app}\game"; Flags: nowait postinstall skipifsilent
