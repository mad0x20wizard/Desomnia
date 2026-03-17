[Code]

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
