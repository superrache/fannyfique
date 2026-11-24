; Installeur Fannyfique (CLAUDE.md 2) : installation par utilisateur, sans droits administrateur.
; Prérequis : publier d'abord l'application en autonome :
;   dotnet publish src/Fannyfique.App -c Release -r win-x64 --self-contained -o publish
; puis compiler ce script avec Inno Setup 6 (ISCC.exe installer\fannyfique.iss).

#define AppName "Fannyfique"
#define AppVersion "0.2.0"
#define AppExe "Fannyfique.exe"

[Setup]
AppId={{6F0B7E3A-58C1-4C69-9E1D-2B7F3C1A9D42}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppName}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=Fannyfique-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\src\Fannyfique.App\Assets\app.ico

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; Les données (%LOCALAPPDATA%\Fannyfique : sources, index, journaux) sont conservées à la désinstallation.
