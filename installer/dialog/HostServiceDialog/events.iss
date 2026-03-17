[Code]

procedure OnSecurityClick(Sender: TObject);
var
  Host: THostConfig;
  Security: TSecurityConfig;
begin
    Host := ConfigureServicesConfig.Hosts[ConfigureServicesConfig.Index];

    Security := Host.Security;

    if EditSecurity(ConfigureServicesDialog, Security) then
    begin        
      Host.Security := Security;
      
      if Host.Security.Method <> '' Then
        Host.Automation.ServiceDemand := 'knock'
      else
        Host.Automation.ServiceDemand := '';

      ConfigureServicesConfig.Hosts[ConfigureServicesConfig.Index] := Host;

      ValidateHost(False);

      UpdateHostServicesForm(ConfigureServicesConfig);
    end;
end;

procedure OnHostKeyDown(Sender: TObject; var Key: Word; Shift: TShiftState);
begin
  Log('Key down');
  ConfigureServicesControls.HostComboTyping := True;
end;

procedure OnHostIndexChange(Sender: TObject);
var
  Text: String;
  Success: Integer;
begin
  if not ConfigureServicesControls.Rebuilding then
  begin
    Log('Changed');

    Text := ConfigureServicesControls.HostCombo.Text;

    if ConfigureServicesControls.HostComboTyping and (Text <> ConfigureServicesConfig.Hosts[ConfigureServicesConfig.Index].Name) then
    begin
      ConfigureServicesConfig.Hosts[ConfigureServicesConfig.Index].Name := Text;
      ConfigureServicesControls.HostCombo.Items[ConfigureServicesConfig.Index] := Text;
      //ConfigureServicesControls.HostCombo.ItemIndex := ConfigureServicesConfig.Index;
      
      ValidateHost(False);
    end
    else if (ConfigureServicesControls.HostCombo.ItemIndex > -1) then
    begin
      if ValidateHost(True) then
      begin
        ConfigureServicesConfig.Index := ConfigureServicesControls.HostCombo.ItemIndex;
        UpdateHostServicesForm(ConfigureServicesConfig);
      end
      else
        ConfigureServicesControls.HostCombo.ItemIndex := ConfigureServicesConfig.Index;
    end;
  end;
end;

procedure OnHostKeyUp(Sender: TObject; var Key: Word; Shift: TShiftState);
begin
  Log('Key up');
  ConfigureServicesControls.HostComboTyping := False;
end;

  
procedure OnMacAddressChange(Sender: TObject);
begin
  if not ConfigureServicesControls.Rebuilding then
  begin
    ConfigureServicesConfig.Hosts[ConfigureServicesConfig.Index].MacAddress := ConfigureServicesControls.MacEdit.Text;

    ValidateHost(False);
  end;
end;

// procedure OnIPAddressChange(Sender: TObject);
// begin
//   if not ConfigureServicesControls.Rebuilding then
//     ConfigureServicesConfig.Hosts[ConfigureServicesConfig.Index].MacAddress := ConfigureServicesControls.MacEdit.Text;
// end;


procedure OnMacAddressAutoChange(Sender: TObject);
begin
  if not ConfigureServicesControls.Rebuilding then
  begin
    ConfigureServicesConfig.Hosts[ConfigureServicesConfig.Index].MacAddress := '';
    ConfigureServicesConfig.Hosts[ConfigureServicesConfig.Index].MacAddressAuto := ConfigureServicesControls.MacAutoCheckbox.Checked;
    ConfigureServicesControls.MacEdit.Enabled := not ConfigureServicesControls.MacAutoCheckbox.Checked;
    ConfigureServicesControls.MacEdit.Text := '';
  end;
end;

procedure OnIPAddressAutoChange(Sender: TObject);
begin
  if not ConfigureServicesControls.Rebuilding then
  begin
    ConfigureServicesConfig.Hosts[ConfigureServicesConfig.Index].IPAddress := IPAddressEmpty;
    ConfigureServicesConfig.Hosts[ConfigureServicesConfig.Index].IPAddressAuto := ConfigureServicesControls.IPAutoCheckbox.Checked;
    EnableWindow(ConfigureServicesControls.IPInput, not ConfigureServicesControls.IPAutoCheckbox.Checked);
    ClearIP(ConfigureServicesControls.IPInput);
  end;
end;



procedure OnHostAdd(Sender: TObject);
var
  Host: THostConfig;
  Count: Integer;

begin
  if ValidateHost(True) then
  begin
    Host.IPAddressAuto := True;
    Host.Automation.Demand := 'wake';

    Count := GetArrayLength(ConfigureServicesConfig.Hosts);
    SetArrayLength(ConfigureServicesConfig.Hosts, Count + 1);
    ConfigureServicesConfig.Hosts[Count] := Host;
    ConfigureServicesConfig.Index := Count;
    
    UpdateHostServicesForm(ConfigureServicesConfig);
        
    ConfigureServicesDialog.ActiveControl := ConfigureServicesControls.HostCombo;

    ConfigureServicesControls.OkButton.ModalResult := 0;

  end;
end;

procedure OnHostRemove(Sender: TObject);
var
  Count: Integer;
  i: Integer;
  j: Integer;

begin
  Count := GetArrayLength(ConfigureServicesConfig.Hosts);
  
  j := 0;
  for i := 0 to Count - 1 do
  begin
    if not (i = ConfigureServicesConfig.Index) then
    begin
      ConfigureServicesConfig.Hosts[j] := ConfigureServicesConfig.Hosts[i];
      j := j + 1;
    end;
  end;

  SetArrayLength(ConfigureServicesConfig.Hosts, j);
  
  if ConfigureServicesConfig.Index > (j - 1) then
    ConfigureServicesConfig.Index := j - 1;
  
  UpdateHostServicesForm(ConfigureServicesConfig);

  ConfigureServicesControls.OkButton.ModalResult := mrOk;
end;

procedure OnServiceAdd(Sender: TObject);
var
  Host: THostConfig;
  Service: TServiceConfig;
  Count: Integer;

begin
  Host := ConfigureServicesConfig.Hosts[ConfigureServicesConfig.Index];

  if PromptService(ConfigureServicesDialog, Service) then
  begin
    Count := GetArrayLength(Host.Services);
    SetArrayLength(Host.Services, Count + 1);
    Host.Services[Count] := Service;
    
    ConfigureServicesConfig.Hosts[ConfigureServicesConfig.Index] := Host;
    
    ValidateHost(False);

    UpdateHostServicesForm(ConfigureServicesConfig);
  end;
end;

procedure OnServiceRemove(Sender: TObject);
var
  Host: THostConfig;
  Count: Integer;
  i: Integer;
  j: Integer;

begin
  Host := ConfigureServicesConfig.Hosts[ConfigureServicesConfig.Index];
  
  Count := GetArrayLength(Host.Services);
  
  j := 0;
  for i := 0 to Count - 1 do
  begin
    if not ConfigureServicesControls.ServicesList.Selected[i] then
    begin
      Host.Services[j] := Host.Services[i];
      j := j+1;
    end;
  end;

  SetArrayLength(Host.Services, j);
  
  ConfigureServicesConfig.Hosts[ConfigureServicesConfig.Index] := Host;
  
  ValidateHost(False);

  UpdateHostServicesForm(ConfigureServicesConfig);
end;

procedure OnHostServiceOK(Sender: TObject);
begin
  ValidateHost(True);
end;
