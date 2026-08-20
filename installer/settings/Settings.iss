[Code]

type
  TSettingsPageControls = record
    ModeTimeoutRadioBox: TNewRadioButton;
    ModeTimeoutDescLabel: TNewStaticText;
    
    TimeoutComboBox: TNewComboBox;
    TimeoutLabel: TNewStaticText;

    PowerCheckbox: TNewCheckBox;
    SleepDelayCombo: TNewComboBox;
    SleepDelayLabel: TNewStaticText;

    RegisterProxyCheckbox: TNewCheckBox;
    WakeAfterCombo: TNewComboBox;
    WakeAfterLabel: TNewStaticText;
    MandatoryHandoffCheckbox: TNewCheckBox;

    DuoCheckbox: TNewCheckBox;
    DuoStopDelayCombo: TNewComboBox;
    DuoStopDelayLabel: TNewStaticText;

    ModeNetworkRadioBox: TNewRadioButton;
    ModeNetworkDescLabel: TNewStaticText;

    PromiscuousCheckbox: TNewCheckBox;
    AdvertiseProxyCheckbox: TNewCheckBox;
    OpenRegistrationCheckbox: TNewCheckBox;
  end;
  
var
  SettingsPage: TWizardPage;
  SettingsControls: TSettingsPageControls;

  
procedure SettingsChanged(Sender: TObject);
begin
  with SettingsControls do
  begin

    // Optional feature only applies to Mode B
    PromiscuousCheckbox.Enabled := ModeNetworkRadioBox.Checked;
    TimeoutComboBox.Enabled := ModeTimeoutRadioBox.Checked;
    TimeoutLabel.Enabled := ModeTimeoutRadioBox.Checked;
    PowerCheckbox.Enabled := ModeTimeoutRadioBox.Checked;
    DuoCheckbox.Enabled := ModeTimeoutRadioBox.Checked and IsComponentSelected('plugins\DuoStreamIntegration');
    
    if not PromiscuousCheckbox.Enabled then
      PromiscuousCheckbox.Checked := False;
    if not PowerCheckbox.Enabled then
      PowerCheckbox.Checked := False;
    if not DuoCheckbox.Enabled then
      DuoCheckbox.Checked := False;
      
    SleepDelayCombo.Enabled := PowerCheckbox.Checked;
    SleepDelayLabel.Enabled := PowerCheckbox.Checked;

    DuoStopDelayCombo.Enabled := DuoCheckbox.Checked;
    DuoStopDelayLabel.Enabled := DuoCheckbox.Checked;

    // Sleep Proxy registration requires power management and network monitoring
    RegisterProxyCheckbox.Enabled := PowerCheckbox.Checked and IsComponentSelected('DesomniaService\NetworkMonitor');

    if not RegisterProxyCheckbox.Enabled then
      RegisterProxyCheckbox.Checked := False;

    WakeAfterCombo.Enabled := RegisterProxyCheckbox.Checked;
    WakeAfterLabel.Enabled := RegisterProxyCheckbox.Checked;

    MandatoryHandoffCheckbox.Enabled := RegisterProxyCheckbox.Checked;

    if not MandatoryHandoffCheckbox.Enabled then
      MandatoryHandoffCheckbox.Checked := False;

    // Sleep Proxy service requires the Wake-on-LAN proxy of Mode B
    AdvertiseProxyCheckbox.Enabled := PromiscuousCheckbox.Checked;

    if not AdvertiseProxyCheckbox.Enabled then
      AdvertiseProxyCheckbox.Checked := False;

    OpenRegistrationCheckbox.Enabled := AdvertiseProxyCheckbox.Checked;

    if not OpenRegistrationCheckbox.Enabled then
      OpenRegistrationCheckbox.Checked := False;
  end;
end;

function CreateLabeledComboBox(Top: Integer; BoxLabel: TNewStaticText): TNewComboBox;
begin
  Result := TNewComboBox.Create(SettingsPage);
  Result.Parent := SettingsPage.Surface;
  Result.Top := Top;
  Result.Width := ScaleX(DurationComboWidth);
  Result.Left := SettingsPage.Surface.Width - Result.Width;

  // force the window handle into existence, so Height reports the actual,
  // font-scaled height instead of the 96-DPI design default
  Result.Items.Clear;

  BoxLabel.Parent := SettingsPage.Surface;
  BoxLabel.WordWrap := False;
  BoxLabel.AutoSize := True;
  BoxLabel.Left := SettingsPage.Surface.Width - Result.Width - BoxLabel.Width - ScaleX(5);
  BoxLabel.Top := Result.Top + (Result.Height - BoxLabel.Height) / 2;

end;

function CreateDurationComboBox(Top: Integer; BoxLabel: TNewStaticText; Immediate: Boolean): TNewComboBox;
begin
  Result := CreateLabeledComboBox(Top, BoxLabel);

  if Immediate Then
    Result.Items.Add('immediate');

  Result.Items.Add('30 s');
  Result.Items.Add('5 min');
  Result.Items.Add('30 min');
  Result.Items.Add('1 h');
end;

function CreateSettingsPage(AfterPageID: Integer): TWizardPage;
var
  TopY: Integer;
  DescLeft: Integer;
  DescWidth: Integer;
  SubLeft: Integer;

begin
  SettingsPage :=
    CreateCustomPage(
      AfterPageID,
      'Select Mode of Operation',
      'How would you like to use Desomnia primarily?');

  with SettingsControls do
  begin
    // Layout constants
    TopY := 0;
    DescLeft := ScaleX(24);
    DescWidth := SettingsPage.SurfaceWidth - DescLeft;
    SubLeft := DescLeft + ScaleX(16);

    // --- Radio 1 ---
    ModeTimeoutRadioBox := TNewRadioButton.Create(SettingsPage);
    ModeTimeoutRadioBox.Parent := SettingsPage.Surface;
    ModeTimeoutRadioBox.Left := 0;
    ModeTimeoutRadioBox.Top := TopY;
    ModeTimeoutRadioBox.Width := SettingsPage.SurfaceWidth;
    ModeTimeoutRadioBox.Height := ScaleY(ModeTimeoutRadioBox.Height);
    ModeTimeoutRadioBox.Caption := 'Monitor resource usage on this computer';
    ModeTimeoutRadioBox.Font.Style := [fsBold];
    
    TimeoutLabel := TNewStaticText.Create(SettingsPage);
    TimeoutLabel.Caption := 'every';
    TimeoutComboBox := CreateDurationComboBox(TopY, TimeoutLabel, False);
    TimeoutComboBox.Text := '5 min';

    TopY := TopY + TimeoutComboBox.Height + ScaleY(5);

    ModeTimeoutDescLabel := TNewStaticText.Create(SettingsPage);
    ModeTimeoutDescLabel.Parent := SettingsPage.Surface;
    ModeTimeoutDescLabel.Left := DescLeft;
    ModeTimeoutDescLabel.Top := TopY;
    ModeTimeoutDescLabel.Width := DescWidth;
    ModeTimeoutDescLabel.AutoSize := True;
    ModeTimeoutDescLabel.WordWrap := True;
    ModeTimeoutDescLabel.Caption :=
      'The usage of selected system resources will be checked periodically. You can configure various actions to be performed when the system becomes idle or when individual resources are demanded.';
    ModeTimeoutDescLabel.Caption := ModeTimeoutDescLabel.Caption + ' This includes automatic Wake-on-LAN for suspended network hosts.';
    
    TopY := TopY + ModeTimeoutDescLabel.Height + ScaleY(10);

    DuoCheckbox := TNewCheckBox.Create(SettingsPage);
    DuoStopDelayCombo := TNewComboBox.Create(SettingsPage);
    DuoStopDelayLabel := TNewStaticText.Create(SettingsPage);
    DuoStopDelayLabel.Caption := 'stop after';

    if IsDuoInstalled then
    begin
      DuoCheckbox.Parent := SettingsPage.Surface;
      DuoCheckbox.Left := DescLeft;      // indent under the radio
      DuoCheckbox.Top := TopY;
      DuoCheckbox.Width := SettingsPage.SurfaceWidth - DescLeft;
      DuoCheckbox.Height := ScaleY(DuoCheckbox.Height);
      DuoCheckbox.Checked := True;

      //DuoCheckbox.Hint := 'When a remote Moonlight client tries to connect to a stopped instance, it will automatically be started.'; //+ #13#10 +
      //DuoCheckbox.ShowHint := True;
      //DuoCheckbox.Cursor := crHelp;



      DuoCheckbox.Caption := 'Start/stop Duo instances automatically';

      DuoStopDelayCombo := CreateDurationComboBox(TopY, DuoStopDelayLabel, True);
      DuoStopDelayCombo.Text := '20 s';

      // TODO: increase tooltip duration
      //DuoStopDelayLabel.Hint := 'The instance will stopped after the configured delay, when the last client has disconnected.';
      //DuoStopDelayLabel.ShowHint := True;
      //DuoStopDelayLabel.Cursor := crHelp;



      TopY := TopY + DuoStopDelayCombo.Height + ScaleY(4);
    end;

    PowerCheckbox := TNewCheckBox.Create(SettingsPage);
    PowerCheckbox.Parent := SettingsPage.Surface;
    PowerCheckbox.Left := DescLeft;      // indent under the radio
    PowerCheckbox.Top := TopY;
    PowerCheckbox.Width := SettingsPage.SurfaceWidth - DescLeft;
    PowerCheckbox.Height := ScaleY(PowerCheckbox.Height);
    PowerCheckbox.Checked := not IsDuoInstalled;

    PowerCheckbox.Caption := 'Replace Windows built-in power management';

    SleepDelayLabel := TNewStaticText.Create(SettingsPage);
    SleepDelayLabel.Caption := 'sleep after';
    SleepDelayCombo := CreateDurationComboBox(TopY, SleepDelayLabel, True);
    SleepDelayCombo.Text := '30 min';

    TopY := TopY + SleepDelayCombo.Height + ScaleY(4);

    // --- Sleep Proxy options under the power checkbox ---
    RegisterProxyCheckbox := TNewCheckBox.Create(SettingsPage);
    RegisterProxyCheckbox.Parent := SettingsPage.Surface;
    RegisterProxyCheckbox.Left := SubLeft;      // indent under the checkbox
    RegisterProxyCheckbox.Top := TopY;
    RegisterProxyCheckbox.Width := SettingsPage.SurfaceWidth - SubLeft;
    RegisterProxyCheckbox.Height := ScaleY(RegisterProxyCheckbox.Height);

    RegisterProxyCheckbox.Caption := 'Automatically register with a Sleep Proxy on the local network';

    WakeAfterLabel := TNewStaticText.Create(SettingsPage);
    WakeAfterLabel.Caption := 'wake after';
    WakeAfterCombo := CreateLabeledComboBox(TopY, WakeAfterLabel);
    WakeAfterCombo.Items.Add('1 h');
    WakeAfterCombo.Items.Add('6 h');
    WakeAfterCombo.Items.Add('12 h');
    WakeAfterCombo.Items.Add('7 days');
    WakeAfterCombo.Text := '7 days';

    TopY := TopY + WakeAfterCombo.Height + ScaleY(4);

    MandatoryHandoffCheckbox := TNewCheckBox.Create(SettingsPage);
    MandatoryHandoffCheckbox.Parent := SettingsPage.Surface;
    MandatoryHandoffCheckbox.Left := SubLeft;      // indent under the checkbox
    MandatoryHandoffCheckbox.Top := TopY;
    MandatoryHandoffCheckbox.Width := SettingsPage.SurfaceWidth - SubLeft;
    MandatoryHandoffCheckbox.Height := ScaleY(MandatoryHandoffCheckbox.Height);

    MandatoryHandoffCheckbox.Caption := 'Only suspend after a successful Sleep Proxy handoff';

    TopY := TopY + MandatoryHandoffCheckbox.Height;

    TopY := TopY + ScaleY(20);
    
    // --- Radio 2 ---
    ModeNetworkRadioBox := TNewRadioButton.Create(SettingsPage);
    ModeNetworkRadioBox.Parent := SettingsPage.Surface;
    ModeNetworkRadioBox.Left := 0;
    ModeNetworkRadioBox.Top := TopY;
    ModeNetworkRadioBox.Width := SettingsPage.SurfaceWidth;
    ModeNetworkRadioBox.Height := ScaleY(ModeNetworkRadioBox.Height);
    ModeNetworkRadioBox.Caption := 'Only wake up remote hosts on the network';
    ModeNetworkRadioBox.Font.Style := [fsBold];

    TopY := TopY + ModeNetworkRadioBox.Height + ScaleY(10);

    ModeNetworkDescLabel := TNewStaticText.Create(SettingsPage);
    ModeNetworkDescLabel.Parent := SettingsPage.Surface;
    ModeNetworkDescLabel.Left := DescLeft;
    ModeNetworkDescLabel.Top := TopY;
    ModeNetworkDescLabel.Width := DescWidth;
    ModeNetworkDescLabel.AutoSize := True;
    ModeNetworkDescLabel.WordWrap := True;
    ModeNetworkDescLabel.Caption :=
      'Network traffic will be monitored in order to automatically wake up suspended network hosts that are accessed from this computer. ';
    TopY := TopY + ModeNetworkDescLabel.Height + ScaleY(10);

    // --- Checkbox under Radio 2 ---
    PromiscuousCheckbox := TNewCheckBox.Create(SettingsPage);
    PromiscuousCheckbox.Parent := SettingsPage.Surface;
    PromiscuousCheckbox.Left := DescLeft;      // indent under the radio
    PromiscuousCheckbox.Top := TopY;
    PromiscuousCheckbox.Width := SettingsPage.SurfaceWidth - DescLeft;
    PromiscuousCheckbox.Height := ScaleY(PromiscuousCheckbox.Height);

    PromiscuousCheckbox.Caption := 'Act as Wake-on-LAN proxy for other hosts on the same local network';

    TopY := TopY + PromiscuousCheckbox.Height + ScaleY(6);

    // --- Sleep Proxy options under the Wake-on-LAN proxy checkbox ---
    AdvertiseProxyCheckbox := TNewCheckBox.Create(SettingsPage);
    AdvertiseProxyCheckbox.Parent := SettingsPage.Surface;
    AdvertiseProxyCheckbox.Left := SubLeft;      // indent under the checkbox
    AdvertiseProxyCheckbox.Top := TopY;
    AdvertiseProxyCheckbox.Width := SettingsPage.SurfaceWidth - SubLeft;
    AdvertiseProxyCheckbox.Height := ScaleY(AdvertiseProxyCheckbox.Height);

    AdvertiseProxyCheckbox.Caption := 'Advertise as Bonjour Sleep Proxy via Multicast DNS';

    TopY := TopY + AdvertiseProxyCheckbox.Height + ScaleY(4);

    OpenRegistrationCheckbox := TNewCheckBox.Create(SettingsPage);
    OpenRegistrationCheckbox.Parent := SettingsPage.Surface;
    OpenRegistrationCheckbox.Left := SubLeft;      // indent under the checkbox
    OpenRegistrationCheckbox.Top := TopY;
    OpenRegistrationCheckbox.Width := SettingsPage.SurfaceWidth - SubLeft;
    OpenRegistrationCheckbox.Height := ScaleY(OpenRegistrationCheckbox.Height);

    OpenRegistrationCheckbox.Caption := 'Allow unknown hosts to register';

    TopY := TopY + ScaleY(18);

    // Defaults
    ModeTimeoutRadioBox.Checked := True;

    // Checkbox should only be usable when Mode B is chosen
    PromiscuousCheckbox.Enabled := False;

    // React to changes
    ModeTimeoutRadioBox.OnClick := @SettingsChanged;
    PowerCheckbox.OnClick := @SettingsChanged;
    RegisterProxyCheckbox.OnClick := @SettingsChanged;
    DuoCheckbox.OnClick := @SettingsChanged;
    ModeNetworkRadioBox.OnClick := @SettingsChanged;
    PromiscuousCheckbox.OnClick := @SettingsChanged;
    AdvertiseProxyCheckbox.OnClick := @SettingsChanged;
    SettingsChanged(nil);
  end;

  Result := SettingsPage;
  
end;

function ShouldConfigureDesomnia(): Boolean;
begin
  if IsReinstall then
    Result := ShouldReconfigure
  else
    Result := not FileExists(ExpandConstant(MONITOR_CONFIG_PATH));
  
  if Debugging then
    Result := True;
end;

<event('ShouldSkipPage')>
function ShouldSkipSettingsPage(PageID: Integer): Boolean;
begin
  Result := False;

  if PageID = SettingsPage.ID then
    if not ShouldConfigureDesomnia then
      Result := True
end;

<event('CurPageChanged')>
procedure UpdateSettingsPage(CurPageID: Integer);
begin
  if CurPageID = SettingsPage.ID then
  begin
    with SettingsControls do
    begin
      ModeNetworkRadioBox.Enabled := IsComponentSelected('DesomniaService\NetworkMonitor');
      ModeNetworkDescLabel.Enabled := ModeNetworkRadioBox.Enabled;
      
      if not ModeNetworkRadioBox.Enabled then
      begin
        ModeNetworkRadioBox.Checked := False;
        ModeTimeoutRadioBox.Checked := True;
      end;
      
      SettingsChanged(nil);
    end;
  end;
end;

function SystemMonitorPrefs(Param: String) : String;
begin
  Result := '';
  
  if SettingsControls.ModeTimeoutRadioBox.Checked then
  begin
    if Param = 'Timeout' then
      Result := SettingsControls.TimeoutComboBox.Text;
    
    if SettingsControls.PowerCheckbox.Checked then
    begin
      if Param = 'IdleAction' then
      begin
        Result := 'sleep';
        if not (SettingsControls.SleepDelayCombo.Text = '') and not (SettingsControls.SleepDelayCombo.Text = 'immediate') then
          Result := Result + '+' + SettingsControls.SleepDelayCombo.Text
      end;
      
      if Param = 'DemandAction' then
        Result := 'sleepless';
    end;
  end;
end;

function ShouldConfigureDuoSessionMonitor(): Boolean;
begin
  if ShouldConfigureDesomnia then
    Result := IsComponentSelected('plugins\DuoStreamIntegration')
  else
    Result := False;
end;

function DuoSessionMonitorPrefs(Param: String): String;
begin
  Result := '';
    
  if SettingsControls.DuoCheckbox.Checked then
  begin
    if Param = 'IdleAction' then
    begin
      Result := 'stop';
      if not (SettingsControls.DuoStopDelayCombo.Text = '') and not (SettingsControls.DuoStopDelayCombo.Text = 'immediate') then
        Result := Result + '+' + SettingsControls.DuoStopDelayCombo.Text
    end;
    
    if Param = 'DemandAction' then
      Result := 'start';
  end;

end;


