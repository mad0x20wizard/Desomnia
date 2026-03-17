[Code]

const
  WC_IPADDRESS = 'SysIPAddress32';

  ICC_INTERNET_CLASSES = $00000800;

  IPM_CLEARADDRESS = $0464;
  IPM_SETADDRESS = $0465;
  IPM_GETADDRESS = $0466;

type
  IPAddress = record
    D: Byte;
    C: Byte;
    B: Byte;
    A: Byte;
  end;

var IPAddressEmpty: IPAddress;

function SendMessageIP(hWnd: HWND; Msg: UINT; wParam: WPARAM; var lParam: IPAddress): LRESULT;
  external 'SendMessageW@user32.dll stdcall';

<event('InitializeWizard')>
procedure InitializeInternetControls; // Ensure the IP control is registered
var
  Icc: TInitCommonControlsEx;
begin
  Icc.dwSize := SizeOf(Icc);
  Icc.dwICC  := ICC_INTERNET_CLASSES;
  InitCommonControlsEx(Icc);
end;

function CreateIPAddressInput(Parent: TWinControl; X, Y, Height, Width: Integer): HWND;
begin
  
  // Create IP address control on the parent
  Result := CreateWindowEx(
    0, WC_IPADDRESS, '',
    WS_CHILD or WS_VISIBLE or WS_TABSTOP,
    X, Y, Width, Height,
    Parent.Handle, 0, 0, 0);
    
  // Apply the wizard font
  SendMessage(Result, WM_SETFONT, DuplicateFont(GetParentForm(Parent).Font.Handle), 1);
  
end;

function IsIPEmpty(IP: IPAddress): Boolean;
begin
  with IP do
  begin
    Result := (A = 0) and (B = 0) and (C = 0) and (D = 0);
  end;
end;

procedure ClearIP(SysIPAddress32: HWND);
begin
  SendMessage(SysIPAddress32, IPM_CLEARADDRESS, 0, 0);
end;

procedure SetIP(SysIPAddress32: HWND; IP: IPAddress);
var
  LPARAM: LongInt;
begin
  with IP do
  begin
    // IPM_SETADDRESS expects: 0x00DDCCBB AA (little-endian packing)
    LPARAM := DWORD(D) or (DWORD(C) shl 8) or (DWORD(B) shl 16) or (DWORD(A) shl 24);
    
    SendMessage(SysIPAddress32, IPM_SETADDRESS, 0, LPARAM);
  end
end;

function GetIP(SysIPAddress32: HWND): IPAddress;
var
  Count: LRESULT;
begin  
  Count := SendMessageIP(SysIPAddress32, IPM_GETADDRESS, 0, Result);
end;

function FormatIP(IP: IPAddress; AppendPrefix: Boolean) : String;
var
  Prefix: Integer;
begin

  with IP do
  begin
    Result := Format('%d.%d.%d.%d', [A, B, C, D]);

    Prefix := 32;
    if D = 0 then
      Prefix := Prefix - 8;
    if C = 0 then
      Prefix := Prefix - 8;
    if B = 0 then
      Prefix := Prefix - 8;
    if A = 0 then
      Prefix := Prefix - 8;
  end;
  
  if Prefix = 0 then
    Result := ''
  else if (Prefix < 32) and AppendPrefix then
    Result := Result + '/' + IntToStr(Prefix);

end;
