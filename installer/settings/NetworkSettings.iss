[Code]

const
  NetworkInterfaceAutomatic = 'Automatic';

type
  TNetworkSettingsPageControls = record    
    IconImage: TBitmapImage;
    
    FSelectNetworkInterfaceLabel: TNewStaticText;
    FNetworkInterfaceHintLabel: TNewStaticText;
    FNpcapLibraryLabel: TNewStaticText;

    NetworkComboBox: TNewComboBox;
    RefreshNetworkButton: TButton;

    IPInput: HWND;
    NetworkIPLabel: TNewStaticText;
    
    HelpIcon: TBitmapImage;
    HelpLabel: TNewStaticText;
    Help2Icon: TBitmapImage;
    Help2Label: TNewStaticText;
    
    Help3Icon: TBitmapImage;
    ConfigureServicesButton: TButton;
    ConfigureRemoteServicesButton: TButton;
  end;
    
type
  TNetworkInterface = record
    Identifier: String;
    Name: String;
  end;

var
  NetworkSettingsPage: TWizardPage;
  NetworkSettingsControls: TNetworkSettingsPageControls;

  NetworkInterfaces: array of TNetworkInterface;
  
  LocalHostConfig: THostServiceConfig;
  RemoteHostsConfig: THostServiceConfig;
  
<event('InitializeWizard')>
procedure InitializeHostServices;
begin
  SetArrayLength(RemoteHostsConfig.Hosts, 0);

  SetArrayLength(LocalHostConfig.Hosts, 1);
  SetArrayLength(LocalHostConfig.Hosts[0].Services, 0);
  LocalHostConfig.Hosts[0].Name := GetComputerNameString;
  LocalHostConfig.Index := 0;
end;

procedure OnConfigureServices(Sender: TObject);
begin
  LocalHostConfig := ConfigureHostServices('Configure services', LocalHostConfig, hsdsHost);
end;

procedure OnConfigureRemoteServices(Sender: TObject);
begin
  RemoteHostsConfig := ConfigureHostServices('Configure remote services', RemoteHostsConfig, hsdsHost or hsdsHostEdit or hsdsHostSelect or hsdsMacEdit or hsdsIPEdit or hsdsSecurity);
end;

procedure RefreshNetworkInterfaces;
var
  ScriptFile: string;
  ResultCode: Integer;
  I: Integer;
  
  ExecOutput: TExecOutput;
  Lines: TArrayOfString;
  Fields: TArrayOfString;
begin
  with NetworkSettingsControls do
  begin

    NetworkIPLabel.Enabled := False;
    EnableWindow(IPInput, False);
    RefreshNetworkButton.Enabled := False;
    NetworkComboBox.Enabled := False;
    NetworkComboBox.Items.Clear();
    
    //NetworkComboBox.Items.Add(NetworkInterfaceAutomatic);

    try
      ExtractTemporaryFile('EnumerateNetworkInterfaces.ps1');
      
      ScriptFile := ExpandConstant('{tmp}\EnumerateNetworkInterfaces.ps1')

      // Run PowerShell script and capture output
      if ExecAndCaptureOutput('powershell.exe', '-ExecutionPolicy Bypass -File "' + ScriptFile + '"', ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, ResultCode, ExecOutput) then
      begin
        Lines := ExecOutput.StdOut;
        
        SetArrayLength(NetworkInterfaces, GetArrayLength(Lines) + 1);

        // Populate ComboBox
        for I := 0 to GetArrayLength(Lines) - 1 do
        begin
          Fields := StringSplit(Lines[I], [';'], stExcludeEmpty);
          
          NetworkInterfaces[I].Identifier := Fields[0];
          NetworkInterfaces[I].Name := Fields[1];
          
          NetworkComboBox.Items.Add(NetworkInterfaces[I].Name);
        end;
      end;
    finally 
      NetworkComboBox.Enabled := True;
      RefreshNetworkButton.Enabled := True;
      EnableWindow(IPInput, True);
      NetworkIPLabel.Enabled := True;
    end;
  end;
end;

procedure SelectDefaultNetworkInterface;
var
  I: Integer;
begin
  with NetworkSettingsControls do
  begin        
    if (NetworkComboBox.Items.Count > 0) and (NetworkComboBox.ItemIndex < 0) then
        NetworkComboBox.ItemIndex := 0;
  end;
end;

procedure OnRefreshNetworkInterfaceList(Sender: TObject);
begin
  RefreshNetworkInterfaces
  SelectDefaultNetworkInterface
end;


function CreateNetworkSettingsPage(AfterPageID: Integer): TWizardPage;
var
  Page: TWizardPage;
  Icon: THandle;
  Success: Boolean;
  TopY: Integer;
begin
  Page := CreateCustomPage(AfterPageID, 'Select Network Interface', 'Which network interface should be used for monitoring?');
  
  with NetworkSettingsControls do
  begin
  
    ExtractTemporaryFile('network.png');
  
    IconImage := TBitmapImage.Create(WizardForm);
    IconImage.Parent := Page.Surface;
    IconImage.Stretch := True;
    IconImage.Left := ScaleX(0);
    IconImage.Top := ScaleY(0);
    IconImage.Width := ScaleX(34);
    IconImage.Height := ScaleY(34);

    IconImage.PngImage.LoadFromFile(ExpandConstant('{tmp}\network.png'));

    
    
    //Success := InitializeBitmapImageFromIcon(IconImage, ExpandConstant('{tmp}\network.ico'), Page.SurfaceColor, [32, 48, 64, 128]); // DOES NOT WORK ANY MORE :(
    
    

    //Success := InitializeBitmapImageFromStockIcon(IconImage, SIID_NETWORKCONNECT, Page.SurfaceColor, []);

    FSelectNetworkInterfaceLabel := TNewStaticText.Create(WizardForm);
    FSelectNetworkInterfaceLabel.Parent := Page.Surface;
    FSelectNetworkInterfaceLabel.Left := ScaleX(44);
    FSelectNetworkInterfaceLabel.Top := ScaleY(10);
    FSelectNetworkInterfaceLabel.Width := ScaleX(373);
    //FSelectNetworkInterfaceLabel.Height := ScaleY(14);
    FSelectNetworkInterfaceLabel.Anchors  := [akLeft, akTop, akRight];
    FSelectNetworkInterfaceLabel.AutoSize  := True;
    FSelectNetworkInterfaceLabel.Caption  := 'Setup will configure the selected network interface.';
    FSelectNetworkInterfaceLabel.ShowAccelChar   := False;
    FSelectNetworkInterfaceLabel.TabOrder   := 0;
    FSelectNetworkInterfaceLabel.WordWrap    := True;
    
    FNetworkInterfaceHintLabel := TNewStaticText.Create(WizardForm);
    FNetworkInterfaceHintLabel.Parent := Page.Surface;
    FNetworkInterfaceHintLabel.Left := WizardForm.SelectDirBrowseLabel.Left;
    FNetworkInterfaceHintLabel.Top := WizardForm.SelectDirBrowseLabel.Top;
    FNetworkInterfaceHintLabel.Width := WizardForm.SelectDirBrowseLabel.Width;
    FNetworkInterfaceHintLabel.Height := WizardForm.SelectDirBrowseLabel.Height;
    FNetworkInterfaceHintLabel.Anchors  := [akLeft, akTop, akRight];
    FNetworkInterfaceHintLabel.AutoSize  := False;
    FNetworkInterfaceHintLabel.Caption  := 'To automatically select an interface, click Next. Refresh first, to select a specific one.';
    FNetworkInterfaceHintLabel.ShowAccelChar   := False;
    FNetworkInterfaceHintLabel.TabOrder   := 0;
    FNetworkInterfaceHintLabel.WordWrap    := True;

    if not IsNpcapInstalled() then
    begin
      FNpcapLibraryLabel := TNewStaticText.Create(WizardForm);
      FNpcapLibraryLabel.Parent := Page.Surface;
      FNpcapLibraryLabel.Left := WizardForm.DiskSpaceLabel.Left;
      FNpcapLibraryLabel.Top := WizardForm.DiskSpaceLabel.Top;
      FNpcapLibraryLabel.Width := WizardForm.DiskSpaceLabel.Width;
      FNpcapLibraryLabel.Height := WizardForm.DiskSpaceLabel.Height;
      FNpcapLibraryLabel.Anchors  := [akLeft, akTop, akRight];
      FNpcapLibraryLabel.AutoSize  := False;
      FNpcapLibraryLabel.Caption  := 'Npcap library ('+NPCAP_VERSION+') will be installed, to allow capturing of raw packets.';
      FNpcapLibraryLabel.ShowAccelChar   := False;
      FNpcapLibraryLabel.TabOrder   := 0;
      FNpcapLibraryLabel.WordWrap    := True;
    end;  
    
    // Create a ComboBox for selection
    NetworkComboBox := TNewComboBox.Create(WizardForm);
    NetworkComboBox.Parent := Page.Surface;
    NetworkComboBox.Top := WizardForm.DirEdit.Top;
    NetworkComboBox.Left := WizardForm.DirEdit.Left;
    NetworkComboBox.Width := WizardForm.DirEdit.Width;
    NetworkComboBox.Height := ScaleY(NetworkComboBox.Height)
    NetworkComboBox.Style := csDropDownList;
    NetworkComboBox.Items.Add(NetworkInterfaceAutomatic);
    NetworkComboBox.ItemIndex := 0;
    NetworkComboBox.Enabled := False;

    // Create a button on the custom page
    RefreshNetworkButton := TButton.Create(WizardForm);
    RefreshNetworkButton.Parent := Page.Surface;
    RefreshNetworkButton.Left := WizardForm.DirBrowseButton.Left;
    RefreshNetworkButton.Top :=  NetworkComboBox.Top;
    RefreshNetworkButton.Width := WizardForm.DirBrowseButton.Width;
    RefreshNetworkButton.Height := NetworkComboBox.Height;
    RefreshNetworkButton.Caption := 'Refresh';
    
    TopY := NetworkComboBox.Top; // + NetworkComboBox.Height + ScaleY(10);
    
    NetworkComboBox.Width := NetworkComboBox.Width - ScaleX(128);
    
    IPInput := CreateIPAddressInput(Page.Surface, NetworkComboBox.Left + NetworkComboBox.Width, TopY, RefreshNetworkButton.Height, ScaleX(128));
   
    
    NetworkIPLabel := TNewStaticText.Create(Page);
    NetworkIPLabel.Parent := Page.Surface;
    NetworkIPLabel.AutoSize := True;
    NetworkIPLabel.Caption := 'IP address:';

    NetworkIPLabel.Left := NetworkComboBox.Left + NetworkComboBox.Width - NetworkIPLabel.Width - ScaleX(5);
    NetworkIPLabel.Top := NetworkComboBox.Top + (NetworkComboBox.Height - NetworkIPLabel.Height) / 2;
    NetworkIPLabel.WordWrap := False;
    
    NetworkComboBox.Width := NetworkComboBox.Width - NetworkIPLabel.Width - ScaleX(15);
    
   TopY := TopY + NetworkComboBox.Height + ScaleY(20);

     HelpIcon := TBitmapImage.Create(WizardForm);
    HelpIcon.Parent := Page.Surface;
    HelpIcon.Left := ScaleX(0);
    HelpIcon.Top := TopY;
    HelpIcon.Width := ScaleX(16);
    HelpIcon.Height := ScaleY(16);
    
    Success := InitializeBitmapImageFromStockIcon(HelpIcon, SIID_INFO, Page.SurfaceColor, []);

   
    HelpLabel := TNewStaticText.Create(Page);
    HelpLabel.Parent := Page.Surface;
    HelpLabel.Left := NetworkComboBox.Left + ScaleX(20);
    HelpLabel.Top := TopY;
    HelpLabel.Width := SettingsPage.Surface.Width - ScaleX(20);
    HelpLabel.AutoSize := True;
    HelpLabel.WordWrap := True;
    HelpLabel.Caption :=
      'If you let Desomnia select the interface automatically, all network interfaces with a Standard Gateway configured will be watched. ' + 
      'Multiple interfaces can be monitored this way.';
   
   TopY := TopY + HelpLabel.Height + ScaleY(10);

      
    Help2Icon := TBitmapImage.Create(WizardForm);
    Help2Icon.Parent := Page.Surface;
    Help2Icon.Left := ScaleX(0);
    Help2Icon.Top := TopY;
    Help2Icon.Width := ScaleX(16);
    Help2Icon.Height := ScaleY(16);
    
    Success := InitializeBitmapImageFromStockIcon(Help2Icon, SIID_INFO, Page.SurfaceColor, []);

   
    Help2Label := TNewStaticText.Create(Page);
    Help2Label.Parent := Page.Surface;
    Help2Label.Left := NetworkComboBox.Left + ScaleX(20);
    Help2Label.Top := TopY;
    Help2Label.Width := SettingsPage.Surface.Width - ScaleX(20);
    Help2Label.AutoSize := True;
    Help2Label.WordWrap := True;
    Help2Label.Caption :=
      'You may also specify an IPv4 network address here. Desomnia will only monitor interfaces with a matching address. ' +
      'Leave individual components empty, to match multiple addresses of the same network.';
      
    TopY := TopY + Help2Label.Height + ScaleY(10);
   

   
    ConfigureRemoteServicesButton := TButton.Create(WizardForm);
    ConfigureRemoteServicesButton.Parent := Page.Surface;
    ConfigureRemoteServicesButton.Caption := 'Configure remote services...';
    ConfigureRemoteServicesButton.Width := WizardForm.CalculateButtonWidth([ConfigureRemoteServicesButton.Caption]);
    ConfigureRemoteServicesButton.Height := RefreshNetworkButton.Height;
    ConfigureRemoteServicesButton.Top :=  TopY;
    ConfigureRemoteServicesButton.Left := Page.Surface.Width - ConfigureRemoteServicesButton.Width;
    ConfigureRemoteServicesButton.OnClick := @OnConfigureRemoteServices;


    ConfigureServicesButton := TButton.Create(WizardForm);
    ConfigureServicesButton.Parent := Page.Surface;
    ConfigureServicesButton.Caption := 'Configure services...';

    ConfigureServicesButton.Top :=  TopY;
    ConfigureServicesButton.Width := WizardForm.CalculateButtonWidth([ConfigureServicesButton.Caption]);
    ConfigureServicesButton.Height := RefreshNetworkButton.Height;
    ConfigureServicesButton.Left := ConfigureRemoteServicesButton.Left - ConfigureServicesButton.Width - ScaleX(5);
    ConfigureServicesButton.OnClick := @OnConfigureServices;

    Help3Icon := TBitmapImage.Create(WizardForm);
    Help3Icon.Parent := Page.Surface;
    Help3Icon.Width := ScaleX(16);
    Help3Icon.Height := ScaleY(16);
    Help3Icon.Left := ConfigureServicesButton.Left - Help3Icon.Width - ScaleX(5);;
    Help3Icon.Top := TopY + (ConfigureServicesButton.Height - Help3Icon.Height) / 2;

    Success := InitializeBitmapImageFromStockIcon(Help3Icon, SIID_NETWORKCONNECT, Page.SurfaceColor, []);


    // Assign the click handler
    RefreshNetworkButton.OnClick := @OnRefreshNetworkInterfaceList;
  end;
  
  Result := Page;
end;

function ShouldConfigureNetworkMonitor(): Boolean;
begin
  if ShouldConfigureDesomnia then
    Result := IsComponentSelected('DesomniaService\NetworkMonitor')
  else
    Result := False;
end;

<event('ShouldSkipPage')>
function ShouldSkipNetworkSettingsPage(PageID: Integer): Boolean;
begin
  Result := False;

  if PageID = NetworkSettingsPage.ID then
    if not ShouldConfigureNetworkMonitor then
      Result := True
end;

<event('CurPageChanged')>
procedure UpdateNetworkSettings(CurPageID: Integer);
begin
  if CurPageID = NetworkSettingsPage.ID then
  begin    
    with NetworkSettingsControls do
    begin
      ConfigureServicesButton.Visible := SettingsControls.ModeTimeoutRadioBox.Checked;
      
      if ConfigureServicesButton.Visible then
        Help3Icon.Left := ConfigureServicesButton.Left - Help3Icon.Width - ScaleX(5)
      else
        Help3Icon.Left := ConfigureRemoteServicesButton.Left - Help3Icon.Width - ScaleX(5);
    end;
  end;
end;

function NetworkMonitorPrefs(Param: String) : String;
var
  A, B, C, D: Byte;
  IP: String;
  Prefix: Integer;
begin
  Result := '';
    
  if NetworkSettingsControls.NetworkComboBox.Text <> NetworkInterfaceAutomatic then
  begin
    if Param = 'InterfaceID' then
      Result := NetworkInterfaces[NetworkSettingsControls.NetworkComboBox.ItemIndex].Identifier;
    if Param = 'InterfaceName' then
      Result := NetworkInterfaces[NetworkSettingsControls.NetworkComboBox.ItemIndex].Name;
  end
  else
  begin
    if Param = 'InterfaceName' then
        Result := 'auto';
  end;

  if Param = 'Network' then
    Result := FormatIP(GetIP(NetworkSettingsControls.IPInput), True);

  if (Param = 'Mode') and SettingsControls.PromiscuousCheckbox.Checked then
    Result := 'promiscuous';
  
end;


<event('NextButtonClick')>
function ValidateNetworkPage(CurPage: Integer): Boolean;
begin
  Result := True;

  if (CurPage = NetworkSettingsPage.ID) then
  begin
    Log('IP: ' + NetworkMonitorPrefs('Network'));
  end;
end;


