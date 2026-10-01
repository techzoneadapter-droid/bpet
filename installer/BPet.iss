#define MyAppName "BPet"
#define MyAppVersion GetEnv("BPET_VERSION")
#define MyAppPublisher "BPet"
#define MyAppExeName "BPet.exe"

[Setup]
AppId={{5C2AFDEB-D036-484B-8D54-C00E51252351}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\BPet
DefaultGroupName={#MyAppName}
OutputDir=output
OutputBaseFilename=BPet-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
AppMutex=Local\BPet.SingleInstance

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\BPet"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Mở BPet"; Flags: nowait postinstall skipifsilent
