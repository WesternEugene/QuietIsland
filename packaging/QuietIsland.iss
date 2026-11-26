#define AppName "Quiet Island"
#define AppVersion "1.5.1"
#define AppExeName "QuietIsland.exe"

[Setup]
AppId={{6F98FA41-1225-4C52-A2DB-85F78C762940}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}.0
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
WizardStyle=modern dark hidebevels
WizardBackColor=#121216
WizardImageFile=InstallerArtwork.png
WizardSmallImageFile=
WizardSizePercent=143,124
WizardKeepAspectRatio=no
DisableWelcomePage=no
DisableProgramGroupPage=yes
AlwaysShowDirOnReadyPage=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Messages]
russian.SetupWindowTitle=Quiet Island · Установка
russian.WelcomeLabel1=Quiet Island
russian.WelcomeLabel2=Музыка, поиск файлов и быстрые действия%nна одной панели Windows.
russian.ClickNext=
russian.ButtonNext=&Продолжить
russian.ButtonBack=&Назад
russian.ButtonFinish=&Готово
russian.WizardSelectDir=Папка установки
russian.SelectDirDesc=Выберите, где разместить Quiet Island.
russian.WizardSelectTasks=Параметры установки
russian.SelectTasksDesc=
russian.WizardReady=Всё готово к установке
russian.ReadyLabel1=Проверьте выбранные параметры.
russian.ReadyLabel2a=
russian.ReadyLabel2b=
russian.WizardInstalling=Установка Quiet Island
russian.InstallingLabel=Распаковываем приложение и настраиваем выбранные компоненты.
russian.FinishedHeadingLabel=Quiet Island установлен
russian.FinishedLabel=Панель можно открыть из меню «Пуск».
russian.FinishedLabelNoIcons=Панель можно открыть из меню «Пуск».
russian.ClickFinish=

[CustomMessages]
russian.EverythingTask=Установить Everything и службу поиска
russian.EverythingInstalled=Служба Everything уже установлена.
russian.EverythingMissing=Служба Everything не найдена. Она нужна для поиска файлов.
russian.SetupVersion=Версия %1

[Tasks]
Name: "everything"; Description: "{cm:EverythingTask}"
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; Flags: unchecked
Name: "autostart"; Description: "Запускать Quiet Island при входе в Windows"; Flags: unchecked

[Files]
Source: "InstallerMotion.png"; Flags: dontcopy
Source: "..\artifacts\installer-preview\QuietIslandPreview-FF8B81.png"; Flags: dontcopy
Source: "..\artifacts\installer-preview\QuietIslandPreview-FFC56C.png"; Flags: dontcopy
Source: "..\artifacts\installer-preview\QuietIslandPreview-B4A4FF.png"; Flags: dontcopy
Source: "..\artifacts\installer-preview\QuietIslandPreview-80CDF5.png"; Flags: dontcopy
Source: "..\artifacts\installer-preview\QuietIslandPreview-7CDEBA.png"; Flags: dontcopy
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
Filename: "{tmp}\Everything-Setup-x64.exe"; Parameters: "/S -install-options ""-app-data -disable-run-as-admin -install-run-on-system-startup -install-service"""; StatusMsg: "Установка Everything и службы поиска…"; Flags: shellexec waituntilterminated; Tasks: everything; Check: not EverythingServiceInstalled
Filename: "{commonpf64}\Everything\Everything.exe"; Parameters: "-startup"; Flags: nowait skipifsilent; Tasks: everything; Check: EverythingServiceInstalled and FileExists(ExpandConstant('{commonpf64}\Everything\Everything.exe'))
Filename: "{app}\QuietIsland.exe"; Description: "Запустить Quiet Island"; Flags: nowait postinstall skipifsilent

[Code]
#include "InstallerMotion.iss"
#include "InstallerAppearance.iss"

var
  TasksInitialized: Boolean;

procedure PositionCoverText(Heading, Detail: TNewStaticText; Left, Width, FontSize: Integer);
begin
  Heading.Caption := Trim(Heading.Caption);
  Heading.SetBounds(Left, ScaleY(82), Width, Heading.Height);
  Heading.Font.Size := FontSize;
  WizardForm.AdjustLabelHeight(Heading);
  Detail.Caption := Trim(Detail.Caption);
  Detail.SetBounds(Left, Heading.Top + Heading.Height + ScaleY(10), Width, Detail.Height);
  WizardForm.AdjustLabelHeight(Detail);
end;

procedure InitializeWizard;
var
  Artwork: TBitmapImage;
  VersionLabel: TNewStaticText;
  ContentLeft, ContentWidth, ContentTop: Integer;
begin
  Artwork := TBitmapImage.Create(WizardForm);
  Artwork.Parent := WizardForm.InnerPage;
  Artwork.Bitmap.Assign(WizardForm.WizardBitmapImage.Bitmap);
  Artwork.Stretch := True;
  Artwork.SetBounds(0, 0, WizardForm.WizardBitmapImage.Width, WizardForm.WizardBitmapImage.Height);

  ContentLeft := Artwork.Width + ScaleX(28);
  ContentWidth := WizardForm.ClientWidth - ContentLeft - ScaleX(26);
  PositionCoverText(WizardForm.WelcomeLabel1, WizardForm.WelcomeLabel2, ContentLeft, ContentWidth, 32);
  PositionCoverText(WizardForm.FinishedHeadingLabel, WizardForm.FinishedLabel, ContentLeft, ContentWidth, 25);
  WizardForm.RunList.Left := ContentLeft;
  WizardForm.RunList.Width := ContentWidth;
  WizardForm.YesRadio.Left := ContentLeft;
  WizardForm.YesRadio.Width := ContentWidth;
  WizardForm.NoRadio.Left := ContentLeft;
  WizardForm.NoRadio.Width := ContentWidth;

  WizardForm.MainPanel.SetBounds(ContentLeft, 0, ContentWidth, ScaleY(110));
  WizardForm.WizardSmallBitmapImage.Visible := False;
  WizardForm.PageNameLabel.Font.Size := 21;
  WizardForm.PageNameLabel.SetBounds(0, ScaleY(30), ContentWidth, ScaleY(33));
  WizardForm.PageDescriptionLabel.SetBounds(0, ScaleY(70), ContentWidth, ScaleY(34));
  ContentTop := WizardForm.MainPanel.Height;
  WizardForm.InnerNotebook.SetBounds(ContentLeft, ContentTop, ContentWidth, Artwork.Height - ContentTop - ScaleY(14));

  WizardForm.SelectDirBitmapImage.Visible := False;
  WizardForm.SelectDirLabel.Visible := False;
  WizardForm.SelectDirBrowseLabel.Visible := False;
  WizardForm.DirBrowseButton.Top := ScaleY(8);
  WizardForm.DirEdit.Top := WizardForm.DirBrowseButton.Top +
    (WizardForm.DirBrowseButton.Height - WizardForm.DirEdit.Height) div 2;
  WizardForm.SelectTasksLabel.Visible := False;
  WizardForm.TasksList.Top := ScaleY(6);
  WizardForm.TasksList.MinItemHeight := ScaleY(36);
  WizardForm.ReadyMemo.WordWrap := True;
  WizardForm.ReadyMemo.BorderStyle := bsNone;
  WizardForm.ReadyMemo.ScrollBars := ssVertical;

  WizardForm.BackButton.Left := WizardForm.NextButton.Left - WizardForm.BackButton.Width - ScaleX(8);
  VersionLabel := TNewStaticText.Create(WizardForm);
  VersionLabel.Parent := WizardForm;
  VersionLabel.Caption := FmtMessage(CustomMessage('SetupVersion'), ['{#AppVersion}']);
  VersionLabel.Enabled := False;
  VersionLabel.Left := ScaleX(18);
  VersionLabel.Top := WizardForm.NextButton.Top + (WizardForm.NextButton.Height - VersionLabel.Height) div 2;
  CreateAppearancePage;
  StartArtworkMotion(Artwork);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then SaveAppearanceSelection;
end;

procedure DeinitializeSetup;
begin
  StopArtworkMotion;
  FreeAppearancePreview;
end;

function EverythingServiceInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\Everything');
end;

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
  I: Integer;
  Command: String;
  ServiceInstalled: Boolean;
begin
  ArtworkPageChanged(CurPageID);
  AppearancePageChanged(CurPageID);
  if CurPageID <> wpSelectTasks then Exit;

  ServiceInstalled := EverythingServiceInstalled;
  if ServiceInstalled then
  begin
    WizardSelectTasks('!everything');
    WizardForm.PageDescriptionLabel.Caption := CustomMessage('EverythingInstalled');
  end
  else
    WizardForm.PageDescriptionLabel.Caption := CustomMessage('EverythingMissing');

  for I := 0 to WizardForm.TasksList.Items.Count - 1 do
    if WizardForm.TasksList.ItemCaption[I] = CustomMessage('EverythingTask') then
    begin
      WizardForm.TasksList.ItemEnabled[I] := not ServiceInstalled;
      Break;
    end;

  { Inno has restored the previous tasks. Use current settings on the first
    visit, then keep the user's choices when they go back to this page. }
  if TasksInitialized then Exit;
  TasksInitialized := True;
  if TasksSpecifiedOnCommandLine then Exit;
  if not ServiceInstalled then WizardSelectTasks('everything');
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'QuietIsland', Command) and
     (Trim(Command) <> '') then
    WizardSelectTasks('autostart')
  else
    WizardSelectTasks('!autostart');
end;
