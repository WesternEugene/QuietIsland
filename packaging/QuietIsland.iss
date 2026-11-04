#define AppName "Quiet Island"
#define AppVersion "1.1.0"
#define AppExeName "QuietIsland.exe"

[Setup]
AppId={{6F98FA41-1225-4C52-A2DB-85F78C762940}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Quiet Island

DefaultDirName={localappdata}\Programs\Quiet Island
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041

OutputDir=..\artifacts\installer
OutputBaseFilename=QuietIsland-{#AppVersion}-Setup-win-x64
SetupIconFile=..\src\QuietIsland\Assets\island.ico
UninstallDisplayIcon={app}\{#AppExeName}

Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "everything"; Description: "Установить Everything для поиска файлов"; GroupDescription: "Дополнительные компоненты:"
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки:"; Flags: unchecked

[Files]
Source: "..\artifacts\QuietIsland\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "dependencies\ffmpeg\*"; DestDir: "{app}\tools\ffmpeg"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "dependencies\Everything-Setup-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Tasks: everything

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\Everything-Setup-x64.exe"; Parameters: "/S"; StatusMsg: "Установка Everything…"; Flags: shellexec waituntilterminated; Tasks: everything
Filename: "{commonpf64}\Everything\Everything.exe"; Parameters: "-startup"; Flags: nowait skipifsilent; Tasks: everything; Check: FileExists(ExpandConstant('{commonpf64}\Everything\Everything.exe'))
Filename: "{app}\QuietIsland.exe"; Description: "Запустить Quiet Island"; Flags: nowait postinstall skipifsilent
