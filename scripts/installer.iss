; Inno Setup 6 script for the Imperial Conquest 2 Windows installer.
;
; It packs the self-contained export that scripts/package.ps1 assembles under rendered/export/ic2/
; (game/, data/, assets/) into ImperialConquest2-v<version>-windows-x64-setup.exe. The installer is
; per-user (no administrator rights) into %LOCALAPPDATA%\Programs\Imperial Conquest 2, and upgrades
; an earlier install in place: the AppId below is v0.5.1's published installer's (read back from its
; install record, HKCU\...\Uninstall\{AppId}_is1, on 2026-10-11), so it must never change.
;
; Build (from the repository root, after `pwsh scripts/package.ps1 -Verify`):
;   ISCC.exe /DAppVersion=0.5.2 /DSourceDir=rendered\export\ic2 /DOutputDir=rendered\release scripts\installer.iss
; ISCC.exe is Inno Setup 6.7.3's compiler (operating-guide.md §1.3).

#ifndef AppVersion
  #error Pass the version: /DAppVersion=0.5.2
#endif
#ifndef SourceDir
  #define SourceDir "..\rendered\export\ic2"
#endif
#ifndef OutputDir
  #define OutputDir "..\rendered\release"
#endif

[Setup]
AppId={{5B7E2C1A-6F3D-4E8B-9A41-1C2D3E4F5A6B}
AppName=Imperial Conquest 2
AppVersion={#AppVersion}
AppPublisher=diegoami
AppPublisherURL=https://github.com/diegoami/imperial_conquest_2
DefaultDirName={localappdata}\Programs\Imperial Conquest 2
DefaultGroupName=Imperial Conquest 2
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=ImperialConquest2-v{#AppVersion}-windows-x64-setup
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\game\IC2.MapViewer.exe
WizardStyle=modern

[InstallDelete]
; An upgrade replaces the whole program, so a file the new version no longer ships cannot linger.
; Saved games and settings live under the user's Godot data folder, not here.
Type: filesandordirs; Name: "{app}\game"
Type: filesandordirs; Name: "{app}\data"
Type: filesandordirs; Name: "{app}\assets"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Imperial Conquest 2"; Filename: "{app}\game\IC2.MapViewer.exe"; WorkingDir: "{app}\game"
Name: "{group}\Uninstall Imperial Conquest 2"; Filename: "{uninstallexe}"
Name: "{userdesktop}\Imperial Conquest 2"; Filename: "{app}\game\IC2.MapViewer.exe"; WorkingDir: "{app}\game"

[Run]
Filename: "{app}\game\IC2.MapViewer.exe"; Description: "Start Imperial Conquest 2"; Flags: nowait postinstall skipifsilent
