; Inno Setup script - built by GitHub Actions (see .github/workflows/build.yml)
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

[Setup]
AppId={{9B6E2C41-7D3A-4F58-A1C2-5E8D7F0B3A64}
AppName=NEURAINK
AppVersion={#AppVersion}
AppPublisher=EGY studios
DefaultDirName={autopf}\NEURAINK
DefaultGroupName=NEURAINK
DisableProgramGroupPage=yes
OutputBaseFilename=NEURAINK-Setup-{#AppVersion}
SetupIconFile=..\src\Neuraink.App\Assets\neuraink.ico
AppVerName=NEURAINK {#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern
UninstallDisplayIcon={app}\NEURAINK.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\tools\diwani_triage.py"; DestDir: "{app}\tools"; Flags: ignoreversion

[Icons]
Name: "{group}\NEURAINK"; Filename: "{app}\NEURAINK.exe"
Name: "{autodesktop}\NEURAINK"; Filename: "{app}\NEURAINK.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\NEURAINK.exe"; Description: "{cm:LaunchProgram,NEURAINK}"; Flags: nowait postinstall skipifsilent
