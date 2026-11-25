const
  AppearanceProfile = '{localappdata}\QuietIsland';
  AppearanceFilename = 'setup-appearance.json';
  PreviewAtlasSide = 8;
  PreviewMusicFrames = 31;
  PreviewFrameMs = 40;

var
  AppearancePage: TWizardPage;
  AppearancePreview, AccentSwatch: TBitmapImage;
  PreviewAtlas, PreviewBackground: TBitmap;
  PreviewAtlasAccent: String;
  PreviewLight: Boolean;
  PreviewTimer: UINT_PTR;
  PreviewStarted: Int64;
  PreviewDrawnFrame: Integer;
  AppearanceTheme, AppearanceScale, AppearanceAccent: TNewComboBox;
  ApplyAppearance, PreviewMusic: TNewCheckBox;

function SetupProcessId: DWORD;
  external 'GetCurrentProcessId@kernel32.dll stdcall';
function ReplaceAppearanceFile(const Source, Destination: String; Flags: DWORD): BOOL;
  external 'MoveFileExW@kernel32.dll stdcall';

function SelectedAppearanceTheme: String;
begin
  case AppearanceTheme.ItemIndex of
    1: Result := 'Light';
    2: Result := 'System';
  else
    Result := 'Dark';
  end;
end;

function SelectedAppearanceScale: Integer;
begin
  Result := StrToInt(Copy(AppearanceScale.Text, 1, Length(AppearanceScale.Text) - 1));
end;

function SelectedAppearanceAccent: String;
begin
  case AppearanceAccent.ItemIndex of
    1: Result := '#FFC56C';
    2: Result := '#B4A4FF';
    3: Result := '#80CDF5';
    4: Result := '#7CDEBA';
  else
    Result := '#FF8B81';
  end;
end;

function AppearanceColor(Red, Green, Blue: Integer): TColor;
begin
  Result := Red or (Green shl 8) or (Blue shl 16);
end;

procedure FillPreviewRect(Canvas: TCanvas; Left, Top, Right, Bottom: Integer);
var
  Bounds: TRect;
begin
  Bounds.Left := Left;
  Bounds.Top := Top;
  Bounds.Right := Right;
  Bounds.Bottom := Bottom;
  Canvas.FillRect(Bounds);
end;

procedure DrawAppearanceFrame;
var
  Destination: HDC;
  Frame, W, H, Width, Height, SourceWidth, SourceHeight, SourceX, SourceY: Integer;
begin
  Frame := Ord(PreviewLight) * (PreviewMusicFrames + 1);
  if PreviewMusic.Checked then
  begin
    Frame := Frame + 1;
    if PreviewTimer <> 0 then
      Frame := Frame + ((ArtworkClock - PreviewStarted) div PreviewFrameMs) mod PreviewMusicFrames;
  end;
  if Frame = PreviewDrawnFrame then Exit;

  W := AppearancePreview.Bitmap.Width;
  H := AppearancePreview.Bitmap.Height;
  Destination := AppearancePreview.Bitmap.Canvas.Handle;
  if not CopyArtwork(Destination, 0, 0, W, H, PreviewBackground.Canvas.Handle, 0, 0, $00CC0020) then Exit;
  SourceWidth := PreviewAtlas.Width div PreviewAtlasSide;
  SourceHeight := PreviewAtlas.Height div PreviewAtlasSide;
  SourceX := (Frame mod PreviewAtlasSide) * SourceWidth;
  SourceY := (Frame div PreviewAtlasSide) * SourceHeight;
  Width := (W - ScaleX(24)) * SelectedAppearanceScale div 150;
  Height := Width * SourceHeight div SourceWidth;
  if BlendArtwork(Destination, (W - Width) div 2, 0, Width, Height,
    PreviewAtlas.Canvas.Handle, SourceX, SourceY, SourceWidth, SourceHeight, $01FF0000) then
  begin
    PreviewDrawnFrame := Frame;
    QueueImagePaint(AppearancePreview);
  end;
end;

procedure PreviewTimerTick(Window: HWND; MessageId: UINT; TimerId: UINT_PTR; Time: DWORD);
begin
  if TimerId <> PreviewTimer then Exit;
  if not WizardForm.Visible then Exit;
  if ArtworkWindowMinimized(WizardForm.Handle) then Exit;
  DrawAppearanceFrame;
end;

procedure StopAppearanceTimer;
begin
  if PreviewTimer <> 0 then KillMotionTimer(0, PreviewTimer);
  PreviewTimer := 0;
end;

procedure AppearancePageChanged(PageID: Integer);
begin
  if AppearancePage = nil then Exit;
  if (PageID <> AppearancePage.ID) or not PreviewMusic.Checked then
  begin
    StopAppearanceTimer;
    Exit;
  end;
  if PreviewTimer <> 0 then Exit;
  if not MotionAllowed then Exit;
  PreviewStarted := ArtworkClock;
  PreviewTimer := CreateMotionTimer(0, 0, PreviewFrameMs, CreateCallback(@PreviewTimerTick));
end;

procedure PaintAppearancePreview;
var
  Canvas: TCanvas;
  Theme, Hex: String;
  SystemLight: Cardinal;
  NewAtlas: TBitmap;
  W, H, Y: Integer;
begin
  Theme := SelectedAppearanceTheme;
  PreviewLight := Theme = 'Light';
  if Theme = 'System' then
    PreviewLight := RegQueryDWordValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Themes\Personalize',
      'AppsUseLightTheme', SystemLight) and (SystemLight = 1);
  Hex := SelectedAppearanceAccent;
  AccentSwatch.BackColor := StrToInt('$' + Copy(Hex, 6, 2) + Copy(Hex, 4, 2) + Copy(Hex, 2, 2));
  if (PreviewAtlas = nil) or (PreviewMusic.Checked and (PreviewAtlasAccent <> Hex)) then
  begin
    NewAtlas := LoadEmbeddedAlphaBitmap('QuietIslandPreview-' + Copy(Hex, 2, 6) + '.png');
    if PreviewAtlas <> nil then PreviewAtlas.Free;
    PreviewAtlas := NewAtlas;
    PreviewAtlasAccent := Hex;
  end;
  W := AppearancePreview.Bitmap.Width;
  H := AppearancePreview.Bitmap.Height;
  Canvas := PreviewBackground.Canvas;
  Canvas.Brush.Style := bsSolid;
  for Y := 0 to H - 1 do
  begin
    if PreviewLight then
      Canvas.Brush.Color := AppearanceColor(209 + Y * 20 div H, 227 - Y * 20 div H, 244 + Y * 5 div H)
    else
      Canvas.Brush.Color := AppearanceColor(34 + Y * 25 div H, 49 - Y * 13 div H, 70 - Y * 4 div H);
    FillPreviewRect(Canvas, 0, Y, W, Y + 1);
  end;

  PreviewDrawnFrame := -1;
  DrawAppearanceFrame;
end;

procedure PreviewMusicChanged(Sender: TObject);
begin
  AppearancePageChanged(WizardForm.CurPageID);
  PaintAppearancePreview;
end;

procedure AppearanceChanged(Sender: TObject);
begin
  ApplyAppearance.Checked := True;
  if Sender = AppearanceAccent then
  begin
    PreviewMusic.Checked := True;
    AppearancePageChanged(WizardForm.CurPageID);
  end;
  PaintAppearancePreview;
end;

function AddAppearanceOption(const Caption: String; const Values: array of String;
  Left, Top, Width, Selected: Integer): TNewComboBox;
var
  CaptionLabel: TNewStaticText;
  I: Integer;
begin
  Result := TNewComboBox.Create(AppearancePage);
  Result.Parent := AppearancePage.Surface;
  Result.Style := csDropDownList;
  Result.SetBounds(Left, Top + ScaleY(17), Width, ScaleY(24));
  for I := 0 to GetArrayLength(Values) - 1 do Result.Items.Add(Values[I]);
  Result.ItemIndex := Selected;
  Result.OnChange := @AppearanceChanged;

  CaptionLabel := TNewStaticText.Create(AppearancePage);
  CaptionLabel.Parent := AppearancePage.Surface;
  CaptionLabel.Caption := Caption;
  CaptionLabel.Left := Left;
  CaptionLabel.Top := Top;
  CaptionLabel.FocusControl := Result;
end;

procedure CreateAppearancePage;
var
  Profile, Pending: String;
  Width, Column, Gap, Top: Integer;
begin
  if WizardSilent then Exit;
  AppearancePage := CreateCustomPage(wpSelectTasks, 'Оформление острова',
    'Выберите тему, масштаб и акцент чёлки.');
  Width := AppearancePage.SurfaceWidth;
  AppearancePreview := TBitmapImage.Create(AppearancePage);
  AppearancePreview.Parent := AppearancePage.Surface;
  AppearancePreview.SetBounds(0, 0, Width, ScaleY(104));
  AppearancePreview.Bitmap.Width := Width;
  AppearancePreview.Bitmap.Height := AppearancePreview.Height;
  PreviewBackground := TBitmap.Create;
  PreviewBackground.Width := Width;
  PreviewBackground.Height := AppearancePreview.Height;

  PreviewMusic := TNewCheckBox.Create(AppearancePage);
  PreviewMusic.Parent := AppearancePage.Surface;
  PreviewMusic.Caption := 'Показать чёлку с музыкой';
  PreviewMusic.SetBounds(0, AppearancePreview.Height + ScaleY(7), Width, ScaleY(22));
  PreviewMusic.OnClick := @PreviewMusicChanged;

  Gap := ScaleX(10);
  Column := (Width - 2 * Gap) div 3;
  Top := PreviewMusic.Top + PreviewMusic.Height + ScaleY(12);
  AppearanceTheme := AddAppearanceOption('&Тема', ['Тёмная', 'Светлая', 'Системная'], 0, Top, Column, 0);
  AppearanceScale := AddAppearanceOption('&Масштаб', ['85%', '100%', '125%', '150%'], Column + Gap, Top, Column, 1);
  AppearanceAccent := AddAppearanceOption('&Акцент', ['Коралл', 'Янтарь', 'Сирень', 'Лёд', 'Мята'],
    2 * (Column + Gap), Top, Width - 2 * (Column + Gap), 0);
  AccentSwatch := TBitmapImage.Create(AppearancePage);
  AccentSwatch.Parent := AppearancePage.Surface;
  AccentSwatch.SetBounds(AppearanceAccent.Left + ScaleX(48), Top + ScaleY(3), ScaleX(15), ScaleY(10));

  ApplyAppearance := TNewCheckBox.Create(AppearancePage);
  ApplyAppearance.Parent := AppearancePage.Surface;
  ApplyAppearance.Caption := 'Использовать это оформление при запуске';
  ApplyAppearance.SetBounds(0, AppearanceTheme.Top + AppearanceTheme.Height + ScaleY(13), Width, ScaleY(23));
  Profile := AddBackslash(ExpandConstant(AppearanceProfile));
  Pending := Profile + AppearanceFilename;
  ApplyAppearance.Checked := not (FileExists(Profile + 'state.json') or FileExists(Profile + 'state.backup.json')
    or FileExists(Pending) or FileExists(Pending + '.applying'));
  PaintAppearancePreview;
end;

procedure FreeAppearancePreview;
begin
  StopAppearanceTimer;
  if PreviewBackground <> nil then PreviewBackground.Free;
  if PreviewAtlas <> nil then PreviewAtlas.Free;
end;

procedure SaveAppearanceSelection;
var
  Profile, Pending, Temporary, Json: String;
begin
  if ApplyAppearance = nil then Exit;
  if not ApplyAppearance.Checked then Exit;

  Profile := AddBackslash(ExpandConstant(AppearanceProfile));
  Pending := Profile + AppearanceFilename;
  Temporary := Pending + '.' + IntToStr(SetupProcessId) + '.tmp';
  Json := Format('{"Theme":"%s","ScalePercent":%d,"AccentColor":"%s"}', [
    SelectedAppearanceTheme, SelectedAppearanceScale, SelectedAppearanceAccent]);
  if ForceDirectories(Profile) and SaveStringToFile(Temporary, Json, False) then
    if ReplaceAppearanceFile(Temporary, Pending, $9) then Exit;

  DeleteFile(Temporary);
  Log('Could not save installer appearance.');
  SuppressibleMsgBox('Не удалось сохранить оформление. Его можно выбрать в настройках Quiet Island.',
    mbInformation, MB_OK, IDOK);
end;
