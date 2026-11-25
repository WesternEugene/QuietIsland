const
  ArtworkWidth = 528;
  ArtworkHeight = 1011;
  CapsuleWidth = 480;
  CapsuleHeight = 208;
  CapsuleColumns = 8;
  CapsuleFrames = 80;
  CapsuleFrameMs = 40;

type
  THighContrast = record
    Size, Flags: DWORD;
    Scheme: NativeUInt;
  end;

var
  MotionTimer: UINT_PTR;
  MotionStarted, ArtworkPulseStarted: Int64;
  MotionAtlas, MotionFrame: TBitmap;
  MotionFrameReady: Boolean;
  InnerArtwork, ActiveArtwork: TBitmapImage;

function ArtworkClock: Int64;
  external 'GetTickCount64@kernel32.dll stdcall';
function ArtworkWindowMinimized(Window: HWND): BOOL;
  external 'IsIconic@user32.dll stdcall';
function ReadClientAnimation(Action, Param: UINT; var Enabled: Integer; Flags: UINT): BOOL;
  external 'SystemParametersInfoW@user32.dll stdcall';
function ReadHighContrast(Action, Param: UINT; var Value: THighContrast; Flags: UINT): BOOL;
  external 'SystemParametersInfoW@user32.dll stdcall';
function CreateMotionTimer(Window: HWND; Id: UINT_PTR; Interval: UINT; Callback: NativeInt): UINT_PTR;
  external 'SetTimer@user32.dll stdcall';
function KillMotionTimer(Window: HWND; Id: UINT_PTR): BOOL;
  external 'KillTimer@user32.dll stdcall';
function CopyArtwork(Dest: HDC; X, Y, Width, Height: Integer; Src: HDC; SrcX, SrcY: Integer; Rop: DWORD): BOOL;
  external 'BitBlt@gdi32.dll stdcall';
function BlendArtwork(Dest: HDC; X, Y, Width, Height: Integer; Src: HDC; SrcX, SrcY, SrcWidth, SrcHeight: Integer; Blend: DWORD): BOOL;
  external 'AlphaBlend@msimg32.dll stdcall';
function StretchArtwork(Dest: HDC; X, Y, Width, Height: Integer; Src: HDC; SrcX, SrcY, SrcWidth, SrcHeight: Integer; Rop: DWORD): BOOL;
  external 'StretchBlt@gdi32.dll stdcall';
function SetArtworkStretchMode(Dest: HDC; Mode: Integer): Integer;
  external 'SetStretchBltMode@gdi32.dll stdcall';
function SetArtworkBrushOrigin(Dest: HDC; X, Y: Integer; Previous: NativeUInt): BOOL;
  external 'SetBrushOrgEx@gdi32.dll stdcall';
function InvalidateArtworkRect(Parent: HWND; var Bounds: TRect; Erase: BOOL): BOOL;
  external 'InvalidateRect@user32.dll stdcall';

function LoadEmbeddedAlphaBitmap(const FileName: String): TBitmap;
var
  Path: String;
  Png: TPngImage;
begin
  Path := AddBackslash(ExpandConstant('{tmp}')) + FileName;
  if not FileExists(Path) then ExtractTemporaryFile(FileName);
  Png := TPngImage.Create;
  try
    Png.LoadFromFile(Path);
    Result := TBitmap.Create;
    try
      Result.Assign(Png);
      Result.AlphaFormat := afPremultiplied;
    except
      Result.Free;
      RaiseLastException;
    end;
  finally
    Png.Free;
  end;
end;

procedure QueueImagePaint(Image: TBitmapImage);
var
  Bounds: TRect;
begin
  { The new pixels already cover the entire rectangle. Erasing it first causes a flash. }
  Bounds.Left := Image.Left;
  Bounds.Top := Image.Top;
  Bounds.Right := Image.Left + Image.Width;
  Bounds.Bottom := Image.Top + Image.Height;
  InvalidateArtworkRect(Image.Parent.Handle, Bounds, False);
end;

procedure PresentArtworkFrame(Image: TBitmapImage; Frame: TBitmap);
var
  Destination: HDC;
begin
  if Image.Stretch or (Image.Bitmap.Width <> Image.Width) or (Image.Bitmap.Height <> Image.Height) then
  begin
    Image.Stretch := False;
    Image.Bitmap.Width := Image.Width;
    Image.Bitmap.Height := Image.Height;
    Image.Bitmap.AlphaFormat := afIgnored;
  end;
  Destination := Image.Bitmap.Canvas.Handle;
  SetArtworkStretchMode(Destination, 4); { HALFTONE }
  SetArtworkBrushOrigin(Destination, 0, 0, 0);
  if StretchArtwork(Destination, 0, 0, Image.Width, Image.Height, Frame.Canvas.Handle,
    0, 0, Frame.Width, Frame.Height, $00CC0020) then
    QueueImagePaint(Image);
end;

function MotionAllowed: Boolean;
var
  Enabled: Integer;
  Contrast: THighContrast;
begin
  Result := False;
  if WizardSilent then Exit;
  { SPI_GETCLIENTAREAANIMATION and SPI_GETHIGHCONTRAST }
  if not ReadClientAnimation($1042, 0, Enabled, 0) then Exit;
  if Enabled = 0 then Exit;
  Contrast.Size := SizeOf(Contrast);
  Contrast.Scheme := 0;
  if ReadHighContrast($42, Contrast.Size, Contrast, 0) then
    if (Contrast.Flags and 1) <> 0 then Exit;
  Result := True;
end;

procedure DrawArtworkFrame(Window: HWND; MessageId: UINT; TimerId: UINT_PTR; Time: DWORD);
var
  Now, Elapsed, PulseAge: Int64;
  Index, X, Y, PulseAlpha: Integer;
  Source, Destination: HDC;
begin
  if TimerId <> MotionTimer then Exit;
  if not WizardForm.Visible then Exit;
  if ArtworkWindowMinimized(WizardForm.Handle) then Exit;
  Now := ArtworkClock;
  Elapsed := Now - MotionStarted;
  Index := (Elapsed div CapsuleFrameMs) mod CapsuleFrames;
  X := (ArtworkWidth - CapsuleWidth) div 2;
  Y := (ArtworkHeight - CapsuleHeight) div 2;
  Source := MotionAtlas.Canvas.Handle;
  Destination := MotionFrame.Canvas.Handle;
  if not CopyArtwork(Destination, 0, 0, ArtworkWidth, ArtworkHeight, Source, 0, 0, $00CC0020) then Exit;

  { AC_SRC_ALPHA uses the atlas's premultiplied PNG pixels. }
  PulseAge := Now - ArtworkPulseStarted;
  if PulseAge < 600 then
  begin
    PulseAlpha := Round(115 * Sin(3.14159265 * PulseAge / 600));
    BlendArtwork(Destination, X, Y + 24, CapsuleWidth, CapsuleHeight,
      Source, 0, ArtworkHeight, CapsuleWidth, CapsuleHeight, $01000000 or (PulseAlpha shl 16));
  end;
  if BlendArtwork(Destination, X, Y, CapsuleWidth, CapsuleHeight, Source,
    ArtworkWidth + (Index mod CapsuleColumns) * CapsuleWidth, (Index div CapsuleColumns) * CapsuleHeight,
    CapsuleWidth, CapsuleHeight, $01FF0000) then
  begin
    MotionFrameReady := True;
    PresentArtworkFrame(ActiveArtwork, MotionFrame);
  end;
end;

procedure StartArtworkMotion(Artwork: TBitmapImage);
begin
  if not MotionAllowed then Exit;
  InnerArtwork := Artwork;
  ActiveArtwork := WizardForm.WizardBitmapImage;
  try
    MotionAtlas := LoadEmbeddedAlphaBitmap('InstallerMotion.png');
    MotionFrame := TBitmap.Create;
    MotionFrame.Width := ArtworkWidth;
    MotionFrame.Height := ArtworkHeight;
    MotionStarted := ArtworkClock;
    MotionTimer := CreateMotionTimer(0, 0, 33, CreateCallback(@DrawArtworkFrame));
  except
    Log('Installer artwork animation: ' + GetExceptionMessage);
  end;
end;

procedure ArtworkPageChanged(PageID: Integer);
var
  PreviousArtwork: TBitmapImage;
begin
  if MotionTimer = 0 then Exit;
  PreviousArtwork := ActiveArtwork;
  case PageID of
    wpWelcome: ActiveArtwork := WizardForm.WizardBitmapImage;
    wpFinished: ActiveArtwork := WizardForm.WizardBitmapImage2;
  else
    ActiveArtwork := InnerArtwork;
  end;
  if MotionFrameReady and (ActiveArtwork <> PreviousArtwork) then
    PresentArtworkFrame(ActiveArtwork, MotionFrame);
  ArtworkPulseStarted := ArtworkClock;
end;

procedure StopArtworkMotion;
begin
  if MotionTimer <> 0 then KillMotionTimer(0, MotionTimer);
  MotionTimer := 0;
  if MotionFrame <> nil then MotionFrame.Free;
  if MotionAtlas <> nil then MotionAtlas.Free;
end;
