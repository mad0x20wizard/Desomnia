[Code]

type
  TNetworkSettingsPageControls = record    
    IconImage: TBitmapImage;
    
    FSelectNetworkInterfaceLabel: TNewStaticText;
    FNetworkInterfaceHintLabel: TNewStaticText;
    FNpcapLibraryLabel: TNewStaticText;

    NetworkComboBox: TNewComboBox;
    RefreshNetworkButton: TButton;
  end;
  
type
  TNetworkSettingsConfig = record
    Count: Integer;
    Identifier: String;
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
  
function ReadNetworkSettingsConfig(): TNetworkSettingsConfig;
begin
  with Result do
  begin
    Count := GetIniInt('NetworkMonitor', 'count', 0, 0, 0, ExpandConstant('{tmp}\prefs.ini'));
    Identifier := GetIniString('NetworkMonitor', 'interface', '', ExpandConstant('{tmp}\prefs.ini'));
  end;
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

    RefreshNetworkButton.Enabled := False;
    NetworkComboBox.Enabled := False;
    NetworkComboBox.Items.Clear();

    try
      ExtractTemporaryFile('CheckNetworkInterfaces.ps1');
      
      ScriptFile := ExpandConstant('{tmp}\CheckNetworkInterfaces.ps1')

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
    end;
  end;
end;

function HasMultipleNetworkMonitors(): Boolean;
begin
  with ReadNetworkSettingsConfig do
    if Count > 1 then
      Result := True
    else
      Result := False;
end;

procedure SelectDefaultNetworkInterface;
var
  I: Integer;
begin
  with NetworkSettingsControls do
  begin
    if HasExistingConfig() then
      with ReadNetworkSettingsConfig do
      begin
        for I := 0 to GetArrayLength(NetworkInterfaces) - 1 do
          if NetworkInterfaces[I].Identifier = Identifier then
            NetworkComboBox.ItemIndex := I
      end;
        
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
begin
  // Create a custom page
  Page := CreateCustomPage(AfterPageID, 'Select Network Interface', 'Which network interface should be used for monitoring?');
  
  with NetworkSettingsControls do
  begin

    IconImage := TBitmapImage.Create(WizardForm);
    IconImage.Parent := Page.Surface;
    IconImage.Left := ScaleX(0);
    IconImage.Top := ScaleY(0);
    IconImage.Width := ScaleX(34);
    IconImage.Height := ScaleY(34);
    
    ExtractTemporaryFile('network.ico');
    
    Success := InitializeBitmapImageFromIcon(IconImage, ExpandConstant('{tmp}\network.ico'), Page.SurfaceColor, [32, 48, 64, 128]); // DOES NOT WORK ANY MORE :(
    Success := InitializeBitmapImageFromStockIcon(IconImage, SIID_INTERNET, Page.SurfaceColor, []);

    FSelectNetworkInterfaceLabel := TNewStaticText.Create(WizardForm);
    FSelectNetworkInterfaceLabel.Parent := Page.Surface;
    FSelectNetworkInterfaceLabel.Left := ScaleX(44);
    FSelectNetworkInterfaceLabel.Top := ScaleY(10);
    FSelectNetworkInterfaceLabel.Width := ScaleX(373);
    FSelectNetworkInterfaceLabel.Height := ScaleY(14);
    FSelectNetworkInterfaceLabel.Anchors  := [akLeft, akTop, akRight];
    FSelectNetworkInterfaceLabel.AutoSize  := False;
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
    FNetworkInterfaceHintLabel.Caption  := 'This should be the interface, which most probably is connected to the outside network.';
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
      FNpcapLibraryLabel.Caption  := 'Npcap library ('+NPCAP_VERSION+') will be installed, to allow capturing of raw pakets.';
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
    NetworkComboBox.Style := csDropDownList;
    
    // Create a button on the custom page
    RefreshNetworkButton := TButton.Create(WizardForm);
    RefreshNetworkButton.Parent := Page.Surface;
    RefreshNetworkButton.Left := WizardForm.DirBrowseButton.Left;
    RefreshNetworkButton.Top :=  NetworkComboBox.Top;
    RefreshNetworkButton.Width := WizardForm.DirBrowseButton.Width;
    RefreshNetworkButton.Height := WizardForm.DirBrowseButton.Height;
    RefreshNetworkButton.Caption := 'Refresh';

    // Assign the click handler
    RefreshNetworkButton.OnClick := @OnRefreshNetworkInterfaceList;
  end;
  
  Result := Page;
end;

function ShouldConfigureNetworkMonitor(): Boolean;
begin
  if ShouldConfigureDesomnia then
    Result := IsComponentSelected('DesomniaService\NetworkMonitor') and not HasMultipleNetworkMonitors()
  else
    Result := False;
end;

<event('ShouldSkipPage')>
function ShouldSkipNetworkPage(PageID: Integer): Boolean;
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
    SelectDefaultNetworkInterface
end;

<event('NextButtonClick')>
function ValidateNetworkPage(CurPage: Integer): Boolean;
begin
  Result := True;

  if (CurPage = NetworkSettingsPage.ID) then
    if NetworkSettingsControls.NetworkComboBox.ItemIndex < 0 then
    begin
      MsgBox('Please select a network interface from the list before continuing.', mbError, MB_OK);
      Result := False;
    end;
end;

function NetworkMonitorPrefs(Param: String) : String;
begin
  Result := '???';

  if Param = 'Interface' then
    Result := NetworkInterfaces[NetworkSettingsControls.NetworkComboBox.ItemIndex].Identifier;
  if Param = 'InterfaceName' then
    Result := NetworkInterfaces[NetworkSettingsControls.NetworkComboBox.ItemIndex].Name;

end;