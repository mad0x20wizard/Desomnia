[Code]

// --- Wizard Helper Functions --- //
const
  MF_BYCOMMAND = $00000000;
  MF_BYPOSITION = $00000400;

type
  HMENU = THandle;

function GetSystemMenu(hWnd: HWND; bRevert: BOOL): HMENU; external 'GetSystemMenu@user32.dll stdcall';
function DeleteMenu(hMenu: HMENU; uPosition, uFlags: UINT): BOOL; external 'DeleteMenu@user32.dll stdcall';
function GetMenuItemCount(hMenu: HMENU): Integer; external 'GetMenuItemCount@user32.dll stdcall';

procedure RemoveAboutMenu();
var
  SystemMenu: HMENU;
begin
  // get the menu handle
  SystemMenu := GetSystemMenu(WizardForm.Handle, False);
  // delete the `About Setup` menu (which has ID 9999)
  DeleteMenu(SystemMenu, 9999, MF_BYCOMMAND);
  // delete the separator
  DeleteMenu(SystemMenu, GetMenuItemCount(SystemMenu)-1, MF_BYPOSITION);
end;

type
  HICON = THandle;

  

const
  MAX_ICONS = 1;

function ExtractIconEx(lpFile: String; nIconIndex: Integer; var phiconLarge, phiconSmall: Integer; nIcons: Integer): Integer;
external 'ExtractIconExA@shell32.dll stdcall';

// Importing a Unicode Windows API function.
function ExtractIcon(hInstance: Integer; lpFile: String; nIconIndex: Integer): Integer;
external 'ExtractIconA@shell32.dll stdcall';



  
// +++ Registry Helper Functions +++ //

var
  IsReinstall: Boolean;

function GetHKLM: Integer;
begin
  if IsWin64 then
    Result := HKLM64
  else
    Result := HKLM32;
end;


const
  UninstallKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppId}_is1';
  UninstallStringName = 'UninstallString';

procedure AddUninstallerArguments(Arguments: String);
var
  S: string;
begin
  if RegQueryStringValue(HKEY_LOCAL_MACHINE, UninstallKey, UninstallStringName, S) then
  begin
    S := S + ' ' + Arguments;
    
    if not RegWriteStringValue(HKEY_LOCAL_MACHINE, UninstallKey, UninstallStringName, S) then
      MsgBox('Error adding arguments to uninstaller.', mbError, MB_OK);
  end else
      MsgBox('Error reading arguments of uninstaller from ' + UninstallKey, mbError, MB_OK);
end;

function HasExistingConfig(): Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\config\monitor.xml'));
end;

function ReadExistingConfig(): Boolean;
var
  Command: String;
  Arguments: String;
  ResultCode: Integer;
begin
  Result := False;

  ExtractTemporaryFile('prefs.ini');
  ExtractTemporaryFile('DesomniaServiceConfigurator.exe');

  Command := ExpandConstant('{tmp}\DesomniaServiceConfigurator.exe');
  Arguments := ExpandConstant('read "{app}\config\monitor.xml" "{tmp}\prefs.ini"')
  
  Log('Reading config: ' + Command + ' ' + Arguments);

  if not Exec(Command, Arguments, ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    MsgBox('Failed to read config.', mbError, MB_OK)
  else
    Result := True;
end;

function ShouldConfigureDesomnia(): Boolean;
begin
  Result := IsReinstall or not HasExistingConfig();
end;

function Max(A, B: Integer): Integer;
begin
  if A > B then
    Result := A
  else
    Result := B;
end;

function Min(A, B: Integer): Integer;
begin
  if A < B then
    Result := A
  else
    Result := B;
end;

function VersionGreaterOrEqual(const Required, Current: string): Boolean;
var
  R, C: TArrayOfString;
  I, Ri, Ci: Integer;
begin
  Result := True;

  R := StringSplit(Required, ['.'], stAll);
  C := StringSplit(Current, ['.'], stAll);
  
  for I := 0 to Max(High(R), High(C)) do
  begin
    if I <= High(R) then
      Ri := StrToIntDef(R[I], 0)
    else
      Ri := 0;

    if I <= High(C) then
      Ci := StrToIntDef(C[I], 0)
    else
      Ci := 0;

    if Ci > Ri then
      Result := True
    else if Ci < Ri then
      Result := False;
  end;
end;

