[Code]

const
  CB_SETEXTENDEDUI = $0155;

type
  RECT = record
    Left: Integer;
    Top: Integer;
    Right: Integer;
    Bottom: Integer;
  end;
  
COMBOBOXINFO = record
    cbSize: DWORD;
    rcItem: RECT;
    rcButton: RECT;
    stateButton: DWORD;
    hwndCombo: HWND;
    hwndItem: HWND;
    hwndList: HWND;
  end;
  
function GetComboBoxInfo(hwndCombo: HWND; var pcbi: COMBOBOXINFO): BOOL;
  external 'GetComboBoxInfo@user32.dll stdcall';

function GetComboEditHandle(Combo: TNewComboBox): HWND;
var
  Info: COMBOBOXINFO;
begin
  Result := 0;

  Info.cbSize := SizeOf(Info);
  if GetComboBoxInfo(Combo.Handle, Info) then
    Result := Info.hwndItem;
end;