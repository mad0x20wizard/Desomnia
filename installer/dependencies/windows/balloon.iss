[Code]

const
  EM_SHOWBALLOONTIP = $1503;
  EM_HIDEBALLOONTIP = $1504;
const
  TTI_NONE = 0;
  TTI_INFO = 1;
  TTI_WARNING = 2;
  TTI_ERROR = 3;
  
type
  EDITBALLOONTIP = record
    cbStruct: DWORD;
    pszTitle: String;
    pszText: String;
    ttiIcon: Integer;
  end;
  
function SendMessageBalloonTip(hWnd: HWND; Msg: UINT; wParam: WPARAM; var lParam: EDITBALLOONTIP): LRESULT;
external 'SendMessageW@user32.dll stdcall';

function ShowEditBalloon(Control: TWinControl; Title, Text: String; Icon: Integer): LRESULT;
var
  Tip: EDITBALLOONTIP;
  Handle: Longint;
begin
  Tip.cbStruct := SizeOf(Tip);
  Tip.pszTitle := Title;
  Tip.pszText := Text;
  Tip.ttiIcon := Icon;
  
  Handle := Control.Handle;
  
  if Control is TNewComboBox then
  begin
    Handle := GetComboEditHandle(TNewComboBox(Control));
  end;

  Result := SendMessageBalloonTip(Handle, EM_SHOWBALLOONTIP, 0, Tip);
end;

procedure HideEditBalloon(Control: TWinControl);
begin
  SendMessage(Control.Handle, EM_HIDEBALLOONTIP, 0, 0);
end;
