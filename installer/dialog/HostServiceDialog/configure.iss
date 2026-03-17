[Code]

procedure UpdateHostServicesForm(Config: THostServiceConfig);
var
  I: Integer;
  Host: THostConfig;
begin
  
  with ConfigureServicesControls do
  begin
    Rebuilding := True;

    HostCombo.Text := '';
    HostCombo.Items.Clear();
    HostCombo.Enabled := False;
    HostCombo.ItemIndex := -1;
    HostRemoveButton.Enabled := False;
    MacEdit.Text := '';
    MacAutoCheckbox.Checked := False;
    ClearIP(IPInput);
    IPAutoCheckbox.Checked := False;
    ServicesList.Items.Clear();
    SetHostControlsEnabled(False);
    SecurityIcon.Visible := False;
    
    for I := 0 to GetArrayLength(Config.Hosts) - 1 do
    begin      
      HostCombo.Items.Add(Config.Hosts[I].Name);
      HostCombo.Enabled := True;
      HostRemoveButton.Enabled := True;
    end;
    
    if GetArrayLength(Config.Hosts) > Config.Index then
    begin
      HostCombo.ItemIndex := Config.Index;
    end;
      
    if HostCombo.ItemIndex > -1 then
    begin
      Host := Config.Hosts[HostCombo.ItemIndex];

      SetHostControlsEnabled(True);

      MacEdit.Text := Host.MacAddress;
      MacEdit.Enabled := not Host.MacAddressAuto;
      MacAutoCheckbox.Checked := Host.MacAddressAuto;

      if not IsIPEmpty(Host.IPAddress) then
        SetIP(IPInput, Host.IPAddress)
      else
        ClearIP(IPInput);

      EnableWindow(IPInput, not Host.IPAddressAuto);
      IPAutoCheckbox.Checked := Host.IPAddressAuto;
            
      for I := 0 to GetArrayLength(Host.Services) - 1 do
      begin
        ServicesList.AddGroup(Host.Services[I].Name, Host.Services[I].Port + '/' + LowerCase(Host.Services[I].Protocol), 0, nil);
      end;

      SecurityIcon.Visible := Host.Security.Method <> '';

    end;
    
    if not ((Style and hsdsHostSelect) <> 0) then
      HostCombo.Enabled := False;

    Rebuilding := False;
  end;
end;
