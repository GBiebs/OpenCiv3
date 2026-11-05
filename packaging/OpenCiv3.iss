; The Windows installer for this fork's build of OpenCiv3 (Inno Setup 6).
; build-windows-installer.sh exports the game into SourceDir and runs
; this with AppVersion set. It installs for the current user only (no
; administrator needed), into their Programs folder, with a Start menu
; shortcut and, unless unticked, a desktop one; installing a newer
; build over an older one updates it in place.

#ifndef AppVersion
  #define AppVersion "dev"
#endif
#ifndef SourceDir
  #error SourceDir (the exported game's folder) must be defined
#endif

[Setup]
; (Never change this: it's how Windows knows a newer build is the same app.)
AppId={{681E878F-2D56-4675-94A4-9DA3384F26A4}
AppName=OpenCiv3
AppVersion={#AppVersion}
AppVerName=OpenCiv3 {#AppVersion}
AppPublisher=GBiebs
AppPublisherURL=https://github.com/GBiebs/OpenCiv3
AppSupportURL=https://github.com/GBiebs/OpenCiv3/releases
DefaultDirName={autopf}\OpenCiv3
DefaultGroupName=OpenCiv3
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=OpenCiv3-Setup-{#AppVersion}
SetupIconFile={#SourceDir}\icon.ico
UninstallDisplayIcon={app}\icon.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
; An older build's .NET files go first, so none of them linger.
Type: filesandordirs; Name: "{app}\data_C7_windows_x86_64"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\OpenCiv3"; Filename: "{app}\OpenCiv3.exe"; WorkingDir: "{app}"; IconFilename: "{app}\icon.ico"
Name: "{autodesktop}\OpenCiv3"; Filename: "{app}\OpenCiv3.exe"; WorkingDir: "{app}"; IconFilename: "{app}\icon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\OpenCiv3.exe"; WorkingDir: "{app}"; Description: "{cm:LaunchProgram,OpenCiv3}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; What the game writes beside itself (its settings and log); saved games
; are in Civ3's Saves folder and stay.
Type: files; Name: "{app}\C7.ini"
Type: files; Name: "{app}\log.txt"
