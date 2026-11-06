#define AppName "Quiet Island"
#define AppVersion "1.3.1"
#define AppExeName "QuietIsland.exe"

[Setup]
AppId={{6F98FA41-1225-4C52-A2DB-85F78C762940}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion=1.3.1.0
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
Name: "autostart"; Description: "Запускать Quiet Island при входе в Windows"; GroupDescription: "Автозагрузка:"; Flags: unchecked

[Files]
Source: "..\artifacts\QuietIsland\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "dependencies\Everything-Setup-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Tasks: everything

[InstallDelete]
; Remove only obsolete files distributed by previous versions, never user data.
Type: filesandordirs; Name: "{app}\packaging\dependencies"
Type: files; Name: "{app}\tools\ffmpeg\ffprobe.exe"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "QuietIsland"; ValueData: """{app}\{#AppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "QuietIsland"; Flags: dontcreatekey deletevalue uninsdeletevalue; Tasks: not autostart

[Run]
Filename: "{tmp}\Everything-Setup-x64.exe"; Parameters: "/S"; StatusMsg: "Установка Everything…"; Flags: shellexec waituntilterminated; Tasks: everything
Filename: "{commonpf64}\Everything\Everything.exe"; Parameters: "-startup"; Flags: nowait skipifsilent; Tasks: everything; Check: FileExists(ExpandConstant('{commonpf64}\Everything\Everything.exe'))
Filename: "{app}\QuietIsland.exe"; Description: "Запустить Quiet Island"; Flags: nowait postinstall skipifsilent

[Code]
var
  StartupTaskInitialized: Boolean;

function TasksSpecifiedOnCommandLine: Boolean;
var
  I: Integer;
  Argument: String;
begin
  Result := False;
  for I := 1 to ParamCount do
  begin
    Argument := Lowercase(ParamStr(I));
    if (Pos('/tasks=', Argument) = 1) or
       (Pos('/mergetasks=', Argument) = 1) or
       (Pos('/loadinf=', Argument) = 1) then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
var
  Command: String;
begin
  if (CurPageID <> wpSelectTasks) or StartupTaskInitialized then Exit;
  StartupTaskInitialized := True;
  { The task list has now loaded. Prefer the current setting over a stale
    previous installer selection, but honour explicit unattended choices. }
  if TasksSpecifiedOnCommandLine then Exit;
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'QuietIsland', Command) and
     (Trim(Command) <> '') then
    WizardSelectTasks('autostart')
  else
    WizardSelectTasks('!autostart');
end;
