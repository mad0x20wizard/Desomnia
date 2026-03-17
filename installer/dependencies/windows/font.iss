[Code]

const
  LF_FACESIZE = 32;
  
  WM_SETFONT  = $0030;
  WM_GETFONT  = $0031;

type
  LOGFONT = record
    lfHeight: Integer;
    lfWidth: Integer;
    lfEscapement: Integer;
    lfOrientation: Integer;
    lfWeight: Integer;
    lfItalic: Byte;
    lfUnderline: Byte;
    lfStrikeOut: Byte;
    lfCharSet: Byte;
    lfOutPrecision: Byte;
    lfClipPrecision: Byte;
    lfQuality: Byte;
    lfPitchAndFamily: Byte;
    lfFaceName: array[0..LF_FACESIZE - 1] of Char;
  end;

function GetObject(h: LongWord; cbBuffer: Integer; var lpvObject: LOGFONT): Integer;
  external 'GetObjectW@gdi32.dll stdcall';
function CreateFontIndirect(const lpLogFont: LOGFONT): LongWord;
  external 'CreateFontIndirectW@gdi32.dll stdcall';
function DeleteObject(hObject: LongWord): BOOL;
  external 'DeleteObject@gdi32.dll stdcall';

function DuplicateFont(FontHandle: LongWord): LongWord;
var
  LF: LOGFONT;
begin
  Result := 0;

  if FontHandle = 0 then
    Exit;

  if GetObject(FontHandle, SizeOf(LF), LF) <> 0 then
    Result := CreateFontIndirect(LF);
end;

var
  TestCanvas: TCanvas;

<event('InitializeWizard')>
procedure InitializeTestCanvas;
// var
begin
  // WizardForm.Canvas.Font := WizardForm.Font;

end;


type
  TSize = record
    cx: Integer;
    cy: Integer;
  end;

function GetDC(hWnd: Integer): Integer;
  external 'GetDC@user32.dll stdcall';
function ReleaseDC(hWnd: Integer; hDC: Integer): Integer;
  external 'ReleaseDC@user32.dll stdcall';
function SelectObject(hDC: Integer; hObject: Integer): Integer;
  external 'SelectObject@gdi32.dll stdcall';
function GetTextExtentPoint32(hDC: Integer; lpString: String; cbString: Integer;
  var lpSize: TSize): Boolean;
  external 'GetTextExtentPoint32W@gdi32.dll stdcall';

function GetTextWidthForControl(const S: String; ControlHandle: Integer): Integer;
var
  DC: Integer;
  FontHandle: Integer;
  OldObj: Integer;
  Size: TSize;
begin
  Result := 0;

  DC := GetDC(0);
  if DC = 0 then
    Exit;

  try
    FontHandle := SendMessage(ControlHandle, WM_GETFONT, 0, 0);
    if FontHandle <> 0 then
      OldObj := SelectObject(DC, FontHandle)
    else
      OldObj := 0;

    if GetTextExtentPoint32(DC, S, Length(S), Size) then
      Result := Size.cx;

    if (FontHandle <> 0) and (OldObj <> 0) then
      SelectObject(DC, OldObj);
  finally
    ReleaseDC(0, DC);
  end;
end;

function PadRightForControlExactFit(const S: String; TargetWidth: Integer; ControlHandle: Longint; Center:Boolean): String;
var
  Candidate: String;
begin
  Result := S;

  while True do
  begin
    if Center then
      Candidate := ' ' + Result + ' '
    else
      Candidate := Result + ' ';

    if GetTextWidthForControl(Candidate, ControlHandle) > TargetWidth then
      Break;

    Result := Candidate;
  end;
end;