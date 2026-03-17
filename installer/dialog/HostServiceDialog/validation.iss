[Code]

function IsHexChar(const C: String): Boolean;
begin
  Result :=
    ((C >= '0') and (C <= '9')) or
    ((C >= 'A') and (C <= 'F')) or
    ((C >= 'a') and (C <= 'f'));
end;

function NormalizeMACAddress(const S: String; var Normalized: String): Boolean;
var
  I: Integer;
  HexOnly: String;
  Ch: String;
begin
  Result := False;
  Normalized := '';
  HexOnly := '';

  { Collect only hex digits, allow common separators }
  for I := 1 to Length(S) do
  begin
    Ch := Copy(S, I, 1);

    if IsHexChar(Ch) then
      HexOnly := HexOnly + Ch
    else if (Ch = ':') or (Ch = '-') or (Ch = '.') or (Ch = ' ') then
    begin
      { allowed separator, ignore }
    end
    else
      Exit;
  end;

  { MAC address must contain exactly 12 hex digits }
  if Length(HexOnly) <> 12 then
    Exit;

  HexOnly := UpperCase(HexOnly);

  Normalized :=
    Copy(HexOnly, 1, 2) + ':' +
    Copy(HexOnly, 3, 2) + ':' +
    Copy(HexOnly, 5, 2) + ':' +
    Copy(HexOnly, 7, 2) + ':' +
    Copy(HexOnly, 9, 2) + ':' +
    Copy(HexOnly, 11, 2);

  Result := True;
end;

function ValidateMACAddress(const S: String): Boolean;
var
  Dummy: String;
begin
  Result := NormalizeMACAddress(S, Dummy);
end;

function ValidateHost(ShowError: Boolean) : Boolean;
var
  Host: THostConfig;

begin


  with ConfigureServicesControls do if not Rebuilding then
  begin
    Result := False;

    OkButton.ModalResult := 0;

    if HostCombo.Enabled then
    begin

      ConfigureServicesConfig.Hosts[ConfigureServicesConfig.Index].IPAddress := GetIP(IPInput);

      Host := ConfigureServicesConfig.Hosts[ConfigureServicesConfig.Index];

      if Host.Name = '' then
      begin
        if ShowError then
          ShowEditBalloon(HostCombo, 'Invalid hostname', 'The hostname must not be empty.', TTI_ERROR);
        Exit;
      end;
    end;

    if (MacEdit.Parent <> nil) and MacEdit.Enabled then
    begin
      if MacEdit.Text = '' then
      begin
        if ShowError then
          ShowEditBalloon(MacEdit, 'Invalid MAC', 'MAC address must not be empty.', TTI_ERROR);
        Exit;
      end;

      if not ValidateMACAddress(MacEdit.Text) then
      begin
        if ShowError then
          ShowEditBalloon(MacEdit, 'Invalid MAC', 'MAC address is not valid.', TTI_ERROR);
        Exit;
      end;

    end;

    OkButton.ModalResult := mrOk;
  end;

  Result := True;
end;

