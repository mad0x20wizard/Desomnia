[Code]

const 
  VirtualHostAutomationPanelWidth = 175;

type
  TVirtualMachineSettingsPageControls = record    
    IconImage: TBitmapImage;
    
    FSelectVirtualMachineProviderLabel: TNewStaticText;
    FVirtualMachineHintLabel: TNewStaticText;

    VirtualMachineProviderComboBox: TNewComboBox;
    VirtualMachineList: TNewCheckListBox;

    AutomationPanel: TPanel;
    AutomationLabel: TNewStaticText;
    AutomationConfigureButton: TButton;
    
    ConfigureServicesIcon: TBitmapImage;
    ConfigureVirtualServicesButton: TButton;
  end;
    
var
  VirtualMachineSettingsPage: TWizardPage;
  VirtualMachineSettingsControls: TVirtualMachineSettingsPageControls;
  
  VirtualHostsConfig: array of THostServiceConfig;
  VirtualHostsConfigStock: array of THostServiceConfig;
  VirtualHostsProviderIndex: Integer;
  
function ShouldConfigureVirtualMachines(): Boolean;
begin
  if ShouldConfigureDesomnia then
  begin
    Result := (GetArrayLength(VirtualProviders) > 0) and IsComponentSelected('plugins\HyperVSupport');

    if not SettingsControls.ModeTimeoutRadioBox.Checked then
      Result := False;
  end
  else
    Result := False;
end;

<event('InitializeWizard')>
procedure InitializeVirtualHostServices;
begin
  SetArrayLength(VirtualHostsConfig, GetArrayLength(VirtualProviders));
  SetArrayLength(VirtualHostsConfigStock, GetArrayLength(VirtualProviders));

  VirtualHostsProviderIndex := -1;
end;

procedure SyncVirtualHostConfig;
var
  I: Integer;
  J: Integer;

begin
  // sync stock config
  for I := 0 to GetArrayLength(VirtualHostsConfig[VirtualHostsProviderIndex].Hosts) - 1 do
    for J := 0 to GetArrayLength(VirtualHostsConfigStock[VirtualHostsProviderIndex].Hosts) - 1 do
      if VirtualHostsConfigStock[VirtualHostsProviderIndex].Hosts[J].Name = VirtualHostsConfig[VirtualHostsProviderIndex].Hosts[I].Name then
        VirtualHostsConfigStock[VirtualHostsProviderIndex].Hosts[J] := VirtualHostsConfig[VirtualHostsProviderIndex].Hosts[I];

end;

function LabelVirtualMachineProvider(Name: String): String;
begin

  Result := Name;

  if Name = 'HyperV' then
    Result := 'Microsoft Hyper-V';

end;

procedure OnConfigureVirtualServices(Sender: TObject);

begin
  VirtualHostsConfig[VirtualHostsProviderIndex] := ConfigureHostServices('Configure virtual services', VirtualHostsConfig[VirtualHostsProviderIndex], hsdsHost or hsdsHostSelect or hsdsIPEdit);

  SyncVirtualHostConfig
end;

procedure VirtualMachineListChanged(Sender: TObject);
var
  I: Integer;
  Count: Integer;
  Config: THostServiceConfig;
  Host: THostConfig;
  Auto: String;

begin
  Config := VirtualHostsConfig[VirtualHostsProviderIndex];

  with VirtualMachineSettingsControls do with VirtualProviders[VirtualHostsProviderIndex] do
  begin
    Count := 0;
    SetArrayLength(Config.Hosts, Count);

    for I := 0 to GetArrayLength(Machines) - 1 do
    begin
      if VirtualMachineList.Checked[I] then
      begin
        Count := Count + 1;
        SetArrayLength(Config.Hosts, Count);

        Host := VirtualHostsConfigStock[VirtualHostsProviderIndex].Hosts[I];

        Config.Hosts[Count-1] := Host;
        if (Sender = VirtualMachineList) and VirtualMachineList.Selected[I] then 
          Config.Index := Count-1;

        Auto := '';

        if (Host.Automation.MagicPacket <> '') and (Host.Automation.Demand = '') then
        begin
          Auto := 'wakeonlan';
          if (Host.Automation.Demand <> '') or (Host.Automation.Idle <> '') then
            Auto := Auto + ' / ';
        end;


        Auto := Auto + Host.Automation.Demand;
        if (Host.Automation.Demand <> '') and (Host.Automation.Idle <> '') then
          Auto := Auto + ' / ';
        Auto := Auto + Host.Automation.Idle;

        Auto := PadRightForControlExactFit(Auto, AutomationPanel.Width - ScaleX(3), VirtualMachineList.Handle, True);

        VirtualMachineList.ItemSubItem[I] := Auto;
        // VirtualMachineList.SubItemFontStyle[I] := [fsItalic];

        

      end
      else
      begin
        VirtualMachineList.ItemSubItem[I] := '';
      end;
    end;

    AutomationConfigureButton.Enabled := Count > 0;
    ConfigureVirtualServicesButton.Enabled := Count > 0;

    VirtualHostsConfig[VirtualHostsProviderIndex] := Config;
  end;
end;

procedure InitializeVirtualMachines(Index: Integer);
var
  Color: TColor;
  Provider: TVirtualMachineProvider;
  J: Integer;

begin
  Provider := VirtualProviders[Index];

  with VirtualMachineSettingsControls do
  begin
    VirtualMachineProviderComboBox.Enabled := False;

    VirtualMachineList.Items.Clear();
    VirtualMachineList.AddGroup('Enumerating virtual machines...', '', 0, nil);
    //VirtualMachineList.ItemFontStyle[0] := [fsItalic];
    Color :=  VirtualMachineList.Font.Color;
    VirtualMachineList.Font.Color := clDkGray;

    try
      EnumerateVirtualMachines(Provider);

      VirtualProviders[Index] := Provider;
    
      SetArrayLength(VirtualHostsConfigStock[Index].Hosts, GetArrayLength(Provider.Machines));

      for J := 0 to GetArrayLength(Provider.Machines) - 1 do
      begin
        VirtualHostsConfigStock[Index].Hosts[J].Name := Provider.Machines[J].Name
        VirtualHostsConfigStock[Index].Hosts[J].MacAddressAuto := True;
        VirtualHostsConfigStock[Index].Hosts[J].IPAddressAuto := True;
        VirtualHostsConfigStock[Index].Hosts[J].Automation.Demand := 'start';
        VirtualHostsConfigStock[Index].Hosts[J].Automation.MagicPacket := 'start';
        Log(Format('[%d][%d] = ', [Index, J]) + Provider.Machines[J].Name);
      end;

    finally
      VirtualMachineList.Font.Color := Color;

      VirtualMachineProviderComboBox.Enabled := True;

      VirtualMachineList.Items.Clear();
    end;
  end;
end;

procedure OnVirtualMachineProviderChanged(Sender: TObject);
var
  I: Integer;

begin
  with VirtualMachineSettingsControls do
  begin
    if (VirtualMachineProviderComboBox.ItemIndex > -1) and not (VirtualMachineProviderComboBox.ItemIndex = VirtualHostsProviderIndex) then 
    begin
      VirtualHostsProviderIndex := VirtualMachineProviderComboBox.ItemIndex;

      if not VirtualProviders[VirtualHostsProviderIndex].Initialized then
      begin
        Log('Initializing VM provider: ' + VirtualProviders[VirtualHostsProviderIndex].Name )

        InitializeVirtualMachines(VirtualHostsProviderIndex);
      end;

      VirtualMachineList.Items.Clear();

      with VirtualProviders[VirtualHostsProviderIndex] do
      begin

        for I := 0 to GetArrayLength(Machines) - 1 do
        begin
          VirtualMachineList.AddCheckBox(Machines[I].Name, '', 0, False, True, False, False, nil);
          VirtualMachineList.ItemEnabled[I] := Machines[I].IsBridged;
        end;
      end;
    end;
  end;

  VirtualMachineListChanged(nil);
end;

procedure OnConfigureAutomation(Sender: TObject);
begin
  VirtualHostsConfig[VirtualHostsProviderIndex] := ConfigureHostAutomation(VirtualHostsConfig[VirtualHostsProviderIndex]);
  
  SyncVirtualHostConfig;

  VirtualMachineListChanged(nil);
end;

function CreateVirtualMachineSettingsPage(AfterPageID: Integer): TWizardPage;
var
  Page: TWizardPage;
  Icon: THandle;
  TopY: Integer;
  I: Integer;

begin
  Page := CreateCustomPage(AfterPageID, 'Select Virtual Machines', 'Which virtual machines do you want to monitor?');

  
  with VirtualMachineSettingsControls do
  begin
    IconImage := TBitmapImage.Create(WizardForm);
    IconImage.Parent := Page.Surface;
    IconImage.Stretch := True;
    IconImage.Left := ScaleX(0);
    IconImage.Top := ScaleY(0);
    IconImage.Width := ScaleX(34);
    IconImage.Height := ScaleY(34);

    InitializeBitmapImageFromStockIcon(IconImage, SIID_WORLD, Page.SurfaceColor, []);

    FSelectVirtualMachineProviderLabel := TNewStaticText.Create(WizardForm);
    FSelectVirtualMachineProviderLabel.Parent := Page.Surface;
    FSelectVirtualMachineProviderLabel.Left := ScaleX(44);
    FSelectVirtualMachineProviderLabel.Top := ScaleY(10);
    FSelectVirtualMachineProviderLabel.Width := ScaleX(373);
    //FSelectVirtualMachineProviderLabel.Height := ScaleY(14);
    FSelectVirtualMachineProviderLabel.Anchors  := [akLeft, akTop, akRight];
    FSelectVirtualMachineProviderLabel.AutoSize  := True;
    FSelectVirtualMachineProviderLabel.Caption  := 'Setup will configure the selected virtual machines.';
    FSelectVirtualMachineProviderLabel.ShowAccelChar   := False;
    FSelectVirtualMachineProviderLabel.TabOrder   := 0;
    FSelectVirtualMachineProviderLabel.WordWrap    := True;

    FVirtualMachineHintLabel := TNewStaticText.Create(WizardForm);
    FVirtualMachineHintLabel.Parent := Page.Surface;
    FVirtualMachineHintLabel.Left := WizardForm.SelectDirBrowseLabel.Left;
    FVirtualMachineHintLabel.Top := WizardForm.SelectDirBrowseLabel.Top;
    FVirtualMachineHintLabel.Width := WizardForm.SelectDirBrowseLabel.Width;
    FVirtualMachineHintLabel.Height := WizardForm.SelectDirBrowseLabel.Height;
    FVirtualMachineHintLabel.Anchors  := [akLeft, akTop, akRight];
    FVirtualMachineHintLabel.AutoSize  := False;
    FVirtualMachineHintLabel.Caption  := 'First select a virtual machine provider and then check all the virtual machines, you want to monitor.';
    FVirtualMachineHintLabel.ShowAccelChar   := False;
    FVirtualMachineHintLabel.TabOrder   := 0;
    FVirtualMachineHintLabel.WordWrap    := True;

    // Create a ComboBox for selection
    VirtualMachineProviderComboBox := TNewComboBox.Create(WizardForm);
    VirtualMachineProviderComboBox.Parent := Page.Surface;
    VirtualMachineProviderComboBox.Top := WizardForm.DirEdit.Top;
    VirtualMachineProviderComboBox.Left := WizardForm.DirEdit.Left;
    VirtualMachineProviderComboBox.Width := Page.Surface.Width;
    VirtualMachineProviderComboBox.Height := ScaleY(VirtualMachineProviderComboBox.Height)
    VirtualMachineProviderComboBox.Style := csDropDownList;

    VirtualMachineProviderComboBox.OnChange := @OnVirtualMachineProviderChanged;

    for I := 0 to GetArrayLength(VirtualProviders) - 1 do
    begin
      VirtualMachineProviderComboBox.Items.Add(LabelVirtualMachineProvider(VirtualProviders[I].Name));
    end;

    // VirtualMachineProviderComboBox.ItemIndex := -1;

    // VirtualMachineProviderComboBox.Enabled := False;

    AutomationPanel := TPanel.Create(Page);
    AutomationPanel.Top := VirtualMachineProviderComboBox.Top;
    AutomationPanel.Width := ScaleX(VirtualHostAutomationPanelWidth);
    AutomationPanel.Left :=  Page.SurfaceWidth - AutomationPanel.Width;
    // AutomationPanel.Caption := 'TPanel';
    AutomationPanel.Color := clInfoBk;
    AutomationPanel.BevelKind := bkFlat;
    AutomationPanel.BevelOuter := bvNone;
    AutomationPanel.ParentBackground := False;
    AutomationPanel.Parent := Page.Surface;

    VirtualMachineProviderComboBox.Width := VirtualMachineProviderComboBox.Width - AutomationPanel.Width - ScaleX(5);

    AutomationLabel := TNewStaticText.Create(WizardForm);
    AutomationLabel.Parent := AutomationPanel;
    AutomationLabel.AutoSize  := True;
    AutomationLabel.Caption  := 'Automation';

    AutomationLabel.Left := (AutomationPanel.Width - AutomationLabel.Width) / 2 - ScaleX(3);
    AutomationLabel.Top := ScaleY(2);
    AutomationLabel.Height := VirtualMachineProviderComboBox.Height;
    AutomationLabel.Font.Style := [fsBold];

    TopY := VirtualMachineProviderComboBox.Top + VirtualMachineProviderComboBox.Height + ScaleY(5);

    VirtualMachineList := TNewCheckListBox.Create(WizardForm);
    VirtualMachineList.Parent := Page.Surface;
    VirtualMachineList.Left := VirtualMachineProviderComboBox.Left;
    VirtualMachineList.Top := TopY;
    VirtualMachineList.Width := Page.Surface.Width;
    VirtualMachineList.Height := ScaleY(180);
    VirtualMachineList.Flat := False;
    VirtualMachineList.OnClickCheck := @VirtualMachineListChanged;

    AutomationPanel.Height := VirtualMachineList.Height + VirtualMachineProviderComboBox.Height * 2 + ScaleY(5) * 3;


    AutomationConfigureButton := TButton.Create(WizardForm);
    AutomationConfigureButton.Parent := AutomationPanel;
    AutomationConfigureButton.Width := AutomationPanel.Width - ScaleX(10);
    AutomationConfigureButton.Height := VirtualMachineProviderComboBox.Height;
    AutomationConfigureButton.Top :=  AutomationPanel.Height - AutomationConfigureButton.Height - ScaleY(6);
    AutomationConfigureButton.Left := (AutomationPanel.Width - AutomationConfigureButton.Width) / 2;
    AutomationConfigureButton.Style := bsSplitButton;
    AutomationConfigureButton.Caption := 'Configure...';
    AutomationConfigureButton.OnClick := @OnConfigureAutomation;
    AutomationConfigureButton.Enabled := False;


    ConfigureVirtualServicesButton := TButton.Create(WizardForm);
    ConfigureVirtualServicesButton.Parent := Page.Surface;
    ConfigureVirtualServicesButton.Caption := 'Configure virtual services...';

    ConfigureVirtualServicesButton.Top :=  VirtualMachineList.Top + VirtualMachineList.Height + ScaleX(5);
    ConfigureVirtualServicesButton.Width := WizardForm.CalculateButtonWidth([ConfigureVirtualServicesButton.Caption]);
    ConfigureVirtualServicesButton.Height := AutomationConfigureButton.Height;
    ConfigureVirtualServicesButton.Left := AutomationPanel.Left - ConfigureVirtualServicesButton.Width - ScaleX(5);
    ConfigureVirtualServicesButton.OnClick := @OnConfigureVirtualServices;
    ConfigureVirtualServicesButton.Enabled := False;

    ConfigureServicesIcon := TBitmapImage.Create(WizardForm);
    ConfigureServicesIcon.Parent := Page.Surface;
    ConfigureServicesIcon.Width := ScaleX(16);
    ConfigureServicesIcon.Height := ScaleY(16);
    ConfigureServicesIcon.Left := ConfigureVirtualServicesButton.Left - ConfigureServicesIcon.Width - ScaleX(5);;
    ConfigureServicesIcon.Top := ConfigureVirtualServicesButton.Top + (ConfigureVirtualServicesButton.Height - ConfigureServicesIcon.Height) / 2;

    InitializeBitmapImageFromStockIcon(ConfigureServicesIcon, SIID_NETWORKCONNECT, Page.SurfaceColor, []);

    if I > 0 then
    begin
      // VirtualMachineProviderComboBox.ItemIndex := 0;
      // OnVirtualMachineProviderChanged(nil);
    end;
  end;
  
  Result := Page;
end;

<event('ShouldSkipPage')>
function ShouldSkipVirtualMachineSettingsPage(PageID: Integer): Boolean;
begin
  Result := False;

  if PageID = VirtualMachineSettingsPage.ID then
    if not ShouldConfigureVirtualMachines then
      Result := True
end;

// <event('CurPageChanged')>
// procedure UpdateVirtualMachineSettings(CurPageID: Integer);
// begin
//   if CurPageID = VirtualMachineSettingsPage.ID then
//   begin    

//     with VirtualMachineSettingsControls do
//     begin

//     end;
//   end;
// end;

// <event('NextButtonClick')>
// function ValidateVirtualMachinePage(CurPage: Integer): Boolean;
// begin
//   Result := True;

//   if (CurPage = VirtualMachineSettingsPage.ID) then
//   begin
//     Log(NetworkMonitorPrefs('VMs'));
//   end;
// end;


