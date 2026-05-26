; EfmlGen Inno Setup script — internal team use, no signing.
;
; Build:
;   1. Publish self-contained binaries first:
;        dotnet publish src/EfmlGen.Wpf/EfmlGen.Wpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-release/EfmlGen-win-x64
;        dotnet publish src/EfmlGen.Cli/EfmlGen.Cli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-release/EfmlGen-win-x64
;   2. Compile installer:
;        "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" installer.iss
;   Output: publish-release/EfmlGen-Setup-v{version}.exe

#define MyAppName "EfmlGen"
#define MyAppVersion "0.4.2"
#define MyAppPublisher "EfmlGen"
#define MyAppURL "https://github.com/hungngominh/EfmlGen"
#define MyAppExeName "EfmlGen.Designer.exe"
#define MyAppCliName "EfmlGen.Cli.exe"
#define SourceDir "publish-release\EfmlGen-win-x64"

[Setup]
AppId={{B5C9F1AD-3D2D-4B2A-9B9A-EFMLGEN-V020}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=
OutputDir=publish-release
OutputBaseFilename=EfmlGen-Setup-v{#MyAppVersion}
SetupIconFile=src\EfmlGen.Wpf\Assets\app.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} v{#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "addtopath"; Description: "Add {#MyAppCliName} folder to PATH (so 'EfmlGen.Cli' works from any terminal)"; GroupDescription: "Command-line:"; Flags: unchecked
Name: "associateefml"; Description: "Associate .efml files with {#MyAppName}"; GroupDescription: "File association:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "DESIGN.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "samples\*"; DestDir: "{app}\samples"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName} Designer"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} CLI (terminal)"; Filename: "cmd.exe"; Parameters: "/K cd /d ""{app}"" && echo Type 'EfmlGen.Cli.exe --help' to get started."; WorkingDir: "{app}"
Name: "{group}\README"; Filename: "{app}\README.md"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName} Designer"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; .efml file association (optional, user opts in via task checkbox)
Root: HKCU; Subkey: "Software\Classes\.efml"; ValueType: string; ValueName: ""; ValueData: "EfmlGen.efml"; Flags: uninsdeletekey; Tasks: associateefml
Root: HKCU; Subkey: "Software\Classes\EfmlGen.efml"; ValueType: string; ValueName: ""; ValueData: "Entity Developer Model"; Flags: uninsdeletekey; Tasks: associateefml
Root: HKCU; Subkey: "Software\Classes\EfmlGen.efml\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Flags: uninsdeletekey; Tasks: associateefml
Root: HKCU; Subkey: "Software\Classes\EfmlGen.efml\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Flags: uninsdeletekey; Tasks: associateefml

; Add app dir to user PATH (optional)
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; Check: NeedsAddPath('{app}'); Tasks: addtopath; Flags: preservestringtype

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
function NeedsAddPath(Param: string): boolean;
var
  OrigPath: string;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', OrigPath) then
  begin
    Result := True;
    exit;
  end;
  Result := Pos(';' + ExpandConstant(Param) + ';', ';' + OrigPath + ';') = 0;
end;
