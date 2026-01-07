[Code]

type
  TSettingsPageControls = record
    HasReadConfig: Boolean;
  
    TimeoutLabel, TimeoutEdit: TLabel;
    TimeoutEditBox: TEdit;

    IdleActionLabel: TLabel;
    IdleActionCombo: TNewComboBox;
    IdleTimeoutLabel: TLabel;
    IdleTimeoutEdit: TEdit;

    UsageActionLabel: TLabel;
    UsageActionCombo: TNewComboBox;

    SeparatorShape: TBevel;

    SessionTrackingLabel: TLabel;
    SessionTrackingCombo: TNewComboBox;

    SleepControlLabel: TLabel;
    SleepControlCombo: TNewComboBox;
  end;
  
type
  TSettingsConfig = record
    Timeout: String;
    IdleAction: String;
    IdleActionTimeout: String;
    UsageAction: String;
    
    TrackSession: String;
    AllowSleepControl: String;
  end;

var
  SettingsPage: TWizardPage;
  SettingsControls: TSettingsPageControls;

function GetSessionMatcher(Box: TNewComboBox): String;
begin
  if Box.Items.Count >= 4 then
    case Box.ItemIndex of
      0: Result := '';
      1: Result := 'user';
      2: Result := 'administrator';
      3: Result := 'everyone';
      4: Result := 'custom';
    else
      Result := '???';
    end
  else if Box.Items.Count = 2 then
    case Box.ItemIndex of
      0: Result := '';
      1: Result := 'everyone';
    else
      Result := '???';
    end;
end;

procedure SetSessionMatcher(Box: TNewComboBox; Matcher: String);
begin
  if Box.Items.Count = 4 then
    case Matcher of
      '': Box.ItemIndex := 0;
      'user': Box.ItemIndex := 1;
      'administrator': Box.ItemIndex := 2;
      'everyone': Box.ItemIndex := 3;
      'custom':
      begin
        Box.Items.Add('Custom');
        Box.ItemIndex := 4;
      end
    else
      Box.ItemIndex := -1;
    end
  else if Box.Items.Count = 2 then
    case Matcher of
      '': Box.ItemIndex := 0;
      'everyone': Box.ItemIndex := 1;
    else
      Box.ItemIndex := -1;
    end
end;


procedure OnSessionTrackingChanged(Sender: TObject);
begin
  with SettingsControls do
    if SessionTrackingCombo.ItemIndex = 0 then
    begin
      SleepControlCombo.Enabled := False;
      SleepControlCombo.ItemIndex := 0;
    end else
    begin
      SleepControlCombo.Enabled := True
    end;
end;

  
function CreateSettingsPage(AfterPageID: Integer): TWizardPage;
begin
  SettingsPage := CreateCustomPage(AfterPageID, 'General settings', 'Here you can configure the basic behaviour of Desomnia.');  

  with SettingsControls do
  begin
    HasReadConfig := False;
  
    // Timeout
    TimeoutLabel := TLabel.Create(SettingsPage);
    TimeoutLabel.Parent := SettingsPage.Surface;
    TimeoutLabel.Caption := 'How often should Desomnia check, if resources are idle?';
    TimeoutLabel.Top := 0;
    TimeoutLabel.Left := 0;

    TimeoutEditBox := TEdit.Create(SettingsPage);
    TimeoutEditBox.Parent := SettingsPage.Surface;
    TimeoutEditBox.Top := TimeoutLabel.Top + TimeoutLabel.Height + ScaleY(5);
    TimeoutEditBox.Left := 0;
    TimeoutEditBox.Width := ScaleX(100);

    // IdleAction
    IdleActionLabel := TLabel.Create(SettingsPage);
    IdleActionLabel.Parent := SettingsPage.Surface;
    IdleActionLabel.Caption := 'What should happen when the computer begins to idle?';
    IdleActionLabel.Top := TimeoutEditBox.Top + TimeoutEditBox.Height + ScaleY(20);
    IdleActionLabel.Left := 0;

    IdleActionCombo := TNewComboBox.Create(SettingsPage);
    IdleActionCombo.Parent := SettingsPage.Surface;
    IdleActionCombo.Top := IdleActionLabel.Top + IdleActionLabel.Height + ScaleY(5);
    IdleActionCombo.Left := 0;
    IdleActionCombo.Width := ScaleX(100);
    IdleActionCombo.Items.Add('');
    IdleActionCombo.Items.Add('sleep');
    IdleActionCombo.Items.Add('shutdown');
    IdleActionCombo.Items.Add('reboot');
    
    IdleTimeoutLabel := TLabel.Create(SettingsPage);
    IdleTimeoutLabel.Parent := SettingsPage.Surface;
    IdleTimeoutLabel.Caption := 'with a delay of';
    IdleTimeoutLabel.Height := IdleActionCombo.Height;
    IdleTimeoutLabel.Top := IdleActionCombo.Top + ScaleY(2);
    IdleTimeoutLabel.Left := IdleActionCombo.Left + IdleActionCombo.Width + ScaleX(5);
    IdleTImeoutLabel.Enabled := False;

    IdleTimeoutEdit := TEdit.Create(SettingsPage);
    IdleTimeoutEdit.Parent := SettingsPage.Surface;
    IdleTimeoutEdit.Top := IdleActionCombo.Top;
    IdleTimeoutEdit.Left := IdleActionCombo.Left + IdleActionCombo.Width + ScaleX(5) + IdleTimeoutLabel.Width + ScaleX(5);
    IdleTimeoutEdit.Width := ScaleX(50);

    // UsageAction
    UsageActionLabel := TLabel.Create(SettingsPage);
    UsageActionLabel.Parent := SettingsPage.Surface;
    UsageActionLabel.Caption := 'What should happen, when the computer is in use?';
    UsageActionLabel.Top := IdleActionCombo.Top + IdleActionCombo.Height + ScaleY(5);
    UsageActionLabel.Left := 0;

    UsageActionCombo := TNewComboBox.Create(SettingsPage);
    UsageActionCombo.Parent := SettingsPage.Surface;
    UsageActionCombo.Top := UsageActionLabel.Top + UsageActionLabel.Height + ScaleY(5);
    UsageActionCombo.Left := 0;
    UsageActionCombo.Width := ScaleX(100);
    UsageActionCombo.Items.Add('');
    UsageActionCombo.Items.Add('sleepless');
    

    // Separator
    SeparatorShape := TBevel.Create(SettingsPage);
    SeparatorShape.Parent := SettingsPage.Surface;
    SeparatorShape.Top := UsageActionCombo.Top + UsageActionCombo.Height + ScaleY(10);
    SeparatorShape.Left := 0;
    SeparatorShape.Width := SettingsPage.Surface.Width;
    SeparatorShape.Height := 2;
    SeparatorShape.Shape := bsTopLine;
    

    // SessionTracking
    SessionTrackingLabel := TLabel.Create(SettingsPage);
    SessionTrackingLabel.Parent := SettingsPage.Surface;
    SessionTrackingLabel.Caption := 'User sessions that will prevent the computer from idling:';
    SessionTrackingLabel.Top := SeparatorShape.Top + SeparatorShape.Height + ScaleY(10);
    SessionTrackingLabel.Left := 0;

    SessionTrackingCombo := TNewComboBox.Create(SettingsPage);
    SessionTrackingCombo.Parent := SettingsPage.Surface;
    //SessionTrackingCombo.Top := SessionTrackingLabel.Top + SessionTrackingLabel.Height + 2;
    SessionTrackingCombo.Top := SessionTrackingLabel.Top;
    SessionTrackingCombo.Left := SettingsPage.Surface.Width - ScaleX(100);
    SessionTrackingCombo.Width := ScaleX(100);
    SessionTrackingCombo.Style := csDropDownList;

    SessionTrackingCombo.Items.Add('None');
    SessionTrackingCombo.Items.Add('Everyone');
    SessionTrackingCombo.ItemIndex := 1;
    
    SessionTrackingCombo.OnChange := @OnSessionTrackingChanged;


    // SleepControl
    SleepControlLabel := TLabel.Create(SettingsPage);
    SleepControlLabel.Parent := SettingsPage.Surface;
    SleepControlLabel.Caption := 'User sessions that will be allowed to control the sleep cycle:';
    SleepControlLabel.Top := SessionTrackingCombo.Top + SessionTrackingCombo.Height + ScaleY(5);
    SleepControlLabel.Left := 0;

    SleepControlCombo := TNewComboBox.Create(SettingsPage);
    SleepControlCombo.Parent := SettingsPage.Surface;
    //SleepControlCombo.Top := SleepControlLabel.Top + SleepControlLabel.Height + 2;
    SleepControlCombo.Top := SleepControlLabel.Top;

    SleepControlCombo.Left := SettingsPage.Surface.Width - ScaleX(100);
    SleepControlCombo.Width := ScaleX(100);
    SleepControlCombo.Style := csDropDownList;

    SleepControlCombo.Items.Add('None');
    SleepControlCombo.Items.Add('Only Me');
    SleepControlCombo.Items.Add('Administrators');
    SleepControlCombo.Items.Add('Everyone');
    SleepControlCombo.ItemIndex := 2;
    
    OnSessionTrackingChanged(SessionTrackingCombo);
  end;
  
  Result := SettingsPage;
end;

function ShouldConfigureSystemMonitor(): Boolean;
begin
  Result := ShouldConfigureDesomnia;
end;

function ShouldConfigureSessionMonitor(): Boolean;
begin
  Result := ShouldConfigureDesomnia;
end;

function ShouldConfigureSleepControl(): Boolean;
begin
  Result := IsComponentSelected('plugins\DesomniaServiceBridge') and ShouldConfigureDesomnia;
end;

<event('ShouldSkipPage')>
function ShouldSkipSettingsPage(PageID: Integer): Boolean;
begin
  Result := False;

  if PageID = SettingsPage.ID then
    if not ShouldConfigureSystemMonitor then
      Result := True;
end;

function ReadSettingsConfig(): TSettingsConfig;
var Idle: TArrayOfString;
begin
  with Result do
  begin
    Timeout := GetIniString('SystemMonitor', 'timeout', '', ExpandConstant('{tmp}\prefs.ini'));
    UsageAction := GetIniString('SystemMonitor', 'usage', '', ExpandConstant('{tmp}\prefs.ini'));
    
    Idle := StringSplit(GetIniString('SystemMonitor', 'idle', '', ExpandConstant('{tmp}\prefs.ini')), ['+'], stExcludeEmpty);
        
    if GetArrayLength(Idle) > 0 then
      IdleAction := Idle[0];
    if GetArrayLength(Idle) > 1 then
      IdleActionTimeout := Idle[1];
      
    TrackSession := GetIniString('SessionMonitor', 'track', '', ExpandConstant('{tmp}\prefs.ini'));
    AllowSleepControl := GetIniString('SessionMonitor', 'allowSleepControl', '', ExpandConstant('{tmp}\prefs.ini'));
  end;
end;

<event('CurPageChanged')>
procedure UpdateSettings(CurPageID: Integer);
var
  ShowSessionTracking: Boolean;
  ShowSleepControl: Boolean;
begin
  if CurPageID = SettingsPage.ID then
  begin
    with SettingsControls do
    begin
  
      if HasExistingConfig() then
      begin
        if not HasReadConfig then with ReadSettingsConfig do
        begin
          TimeoutEditBox.Text := Timeout;
          IdleActionCombo.Text := IdleAction;
          IdleTimeoutEdit.Text := IdleActionTimeout;
          UsageActionCombo.Text := UsageAction;
          
          SetSessionMatcher(SessionTrackingCombo, TrackSession);
          SetSessionMatcher(SleepControlCombo, AllowSleepControl);
          
          HasReadConfig := True;
        end;
      
      end else begin
        TimeoutEditBox.Text := '5min';
        IdleActionCombo.Text := 'sleep';
        IdleTimeoutEdit.Text := '1x';
        UsageActionCombo.Text := 'sleepless';
      end;
      
      ShowSleepControl := IsComponentSelected('plugins\DesomniaServiceBridge');
  
      SleepControlLabel.Visible := ShowSleepControl;
      SleepControlCombo.Visible := ShowSleepControl;
    end;
  end;
end;

function SystemMonitorPrefs(Param: String) : String;
begin
  Result := '???';

  if Param = 'Timeout' then
    Result := SettingsControls.TimeoutEditBox.Text;
    
  if Param = 'IdleAction' then
  begin
    Result := SettingsControls.IdleActionCombo.Text;
    if not (SettingsControls.IdleTimeoutEdit.Text = '') then
      Result := Result + '+' + SettingsControls.IdleTimeoutEdit.Text
  end;
  
  if Param = 'UsageAction' then
    Result := SettingsControls.UsageActionCombo.Text;

end;

function SessionMonitorPrefs(Param: String) : String;
begin
  Result := '???';

  if Param = 'Track' then
    Result := GetSessionMatcher(SettingsControls.SessionTrackingCombo);
  if Param = 'AllowSleepControl' then
    Result := GetSessionMatcher(SettingsControls.SleepControlCombo);

end;

