[Code]

type
  WPARAM = LongInt;
  LPARAM = LongInt;
  LRESULT = LongInt;

const
  WS_CHILD    = 	$40000000;
  WS_VISIBLE  = 	$10000000;
  WS_TABSTOP  = 	$00010000;
  
type
  TInitCommonControlsEx = record
    dwSize: DWORD;
    dwICC: DWORD;
  end;

var
  ComboBoxHeight: Integer;

// Windows User Interface

function InitCommonControlsEx(var icc: TInitCommonControlsEx): BOOL;
  external 'InitCommonControlsEx@comctl32.dll stdcall';

function CreateWindowEx(dwExStyle: DWORD; lpClassName, lpWindowName: string;
  dwStyle: DWORD; X, Y, nWidth, nHeight: Integer;
  hWndParent: HWND; hMenu: HWND; hInstance: HWND; lpParam: Integer): HWND;
  external 'CreateWindowExW@user32.dll stdcall';

function SetParent(hWndChild: HWND; hWndNewParent: HWND): HWND;
  external 'SetParent@user32.dll stdcall';

function EnableWindow(hWnd: HWND; bEnable: BOOL): BOOL;
  external 'EnableWindow@user32.dll stdcall';  
  
function DestroyWindow(hWnd: HWND): BOOL;
  external 'DestroyWindow@user32.dll stdcall';

//function SendMessage(hWnd: HWND; Msg: UINT; wParam: WPARAM; lParam: LPARAM): LRESULT;
  //external 'SendMessageW@user32.dll stdcall';

function CenterLeftBetweenRightY(TextLabel: TControl; Control: TControl) : Integer;
begin
  Result := Control.Top + (Control.Height - TextLabel.Height) / 2;
end;

function GetParentForm(Control: TWinControl): TForm;
begin
  Result := nil;

  while Control <> nil do
  begin
    if Control is TForm then
    begin
      Result := TForm(Control);
      Exit;
    end;

    Control := Control.Parent;
  end;
end;

procedure SetFormWidth(Form: TSetupForm; ClientWidth: Integer);
var
  BorderWidth: Integer;
begin
  BorderWidth := Form.Width - Form.ClientWidth;
  
  Form.Width := BorderWidth + ClientWidth;
end;

procedure SetFormHeight(Form: TSetupForm; ClientHeight: Integer);
var
  TitleBarHeight: Integer;
begin
  TitleBarHeight := Form.Height - Form.ClientHeight;
  
  Form.Height := TitleBarHeight + ClientHeight;
end;

