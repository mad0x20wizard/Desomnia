[Code]

type
  TDuoSettingsPageControls = record
    HasReadConfig: Boolean;

    IdleActionLabel: TLabel;
    IdleActionCombo: TNewComboBox;
    IdleTimeoutLabel: TLabel;
    IdleTimeoutEdit: TEdit;

    DemandActionLabel: TLabel;
    DemandActionCombo: TNewComboBox;
  end;
  
type
  TDuoSettingsConfig = record
    IdleAction: String;
    IdleActionTimeout: String;
    DemandAction: String;
  end;

var
  DuoSettingsPage: TWizardPage;
  DuoSettingsControls: TDuoSettingsPageControls;

function CreateDuoSettingsPage(AfterPageID: Integer): TWizardPage;
begin
  DuoSettingsPage := CreateCustomPage(AfterPageID, 'DuoStream settings', 'Here you can configure the behaviour of the DuoStream Integration.');
  
  with DuoSettingsControls do
  begin
    // IdleAction
    IdleActionLabel := TLabel.Create(DuoSettingsPage);
    IdleActionLabel.Parent := DuoSettingsPage.Surface;
    IdleActionLabel.Caption := 'What should happen when a Duo instance begins to idle?';
    IdleActionLabel.Top := 0;
    IdleActionLabel.Left := 0;

    IdleActionCombo := TNewComboBox.Create(DuoSettingsPage);
    IdleActionCombo.Parent := DuoSettingsPage.Surface;
    IdleActionCombo.Top := IdleActionLabel.Top + IdleActionLabel.Height + ScaleY(5);
    IdleActionCombo.Left := 0;
    IdleActionCombo.Width := ScaleX(100);
    IdleActionCombo.Items.Add('');
    IdleActionCombo.Items.Add('stop');

    IdleTimeoutLabel := TLabel.Create(DuoSettingsPage);
    IdleTimeoutLabel.Parent := DuoSettingsPage.Surface;
    IdleTimeoutLabel.Caption := 'with a delay of';
    IdleTimeoutLabel.Height := IdleActionCombo.Height;
    IdleTimeoutLabel.Top := IdleActionCombo.Top + ScaleY(2);
    IdleTimeoutLabel.Left := IdleActionCombo.Left + IdleActionCombo.Width + ScaleX(5);
    IdleTImeoutLabel.Enabled := False;
    
    IdleTimeoutEdit := TEdit.Create(DuoSettingsPage);
    IdleTimeoutEdit.Parent := DuoSettingsPage.Surface;
    IdleTimeoutEdit.Top := IdleActionCombo.Top;
    IdleTimeoutEdit.Left := IdleActionCombo.Left + IdleActionCombo.Width + ScaleX(5) + IdleTimeoutLabel.Width + ScaleX(5);
    IdleTimeoutEdit.Width := ScaleX(100);

    // UsageAction
    DemandActionLabel := TLabel.Create(DuoSettingsPage);
    DemandActionLabel.Parent := DuoSettingsPage.Surface;
    DemandActionLabel.Caption := 'What should happen, when a Duo instance is accessed on demand?';
    DemandActionLabel.Top := IdleActionCombo.Top + IdleActionCombo.Height + ScaleY(10);
    DemandActionLabel.Left := 0;

    DemandActionCombo := TNewComboBox.Create(DuoSettingsPage);
    DemandActionCombo.Parent := DuoSettingsPage.Surface;
    DemandActionCombo.Top := DemandActionLabel.Top + DemandActionLabel.Height + ScaleY(5);
    DemandActionCombo.Left := 0;
    DemandActionCombo.Width := ScaleX(100);
    DemandActionCombo.Items.Add('');
    DemandActionCombo.Items.Add('start');
  end;

  Result := DuoSettingsPage;
end;

function ShouldConfigureDuoStreamMonitor(): Boolean;
begin
  if ShouldConfigureDesomnia then
    Result := IsComponentSelected('plugins\DuoStreamIntegration')
  else
    Result := False;
end;

<event('ShouldSkipPage')>
function ShouldSkipDuoSettingsPage(PageID: Integer): Boolean;
begin
  Result := False;

  if PageID = DuoSettingsPage.ID then
    if not ShouldConfigureDuoStreamMonitor then
      Result := True;
end;

function ReadDuoSettingsConfig(): TDuoSettingsConfig;
var Idle: TArrayOfString;
begin
  with Result do
  begin
    DemandAction := GetIniString('DuoStreamMonitor', 'demand', '', ExpandConstant('{tmp}\prefs.ini'));
    
    Idle := StringSplit(GetIniString('DuoStreamMonitor', 'idle', '', ExpandConstant('{tmp}\prefs.ini')), ['+'], stExcludeEmpty);
        
    if GetArrayLength(Idle) > 0 then
      IdleAction := Idle[0];
    if GetArrayLength(Idle) > 1 then
      IdleActionTimeout := Idle[1];
  end;
end;

<event('CurPageChanged')>
procedure UpdateDuoSettings(CurPageID: Integer);
begin
  if CurPageID = DuoSettingsPage.ID then
  begin
    with DuoSettingsControls do
    begin
  
      if HasExistingConfig() then
      begin
        if not HasReadConfig then with ReadDuoSettingsConfig do
        begin
          IdleActionCombo.Text := IdleAction;
          IdleTimeoutEdit.Text := IdleActionTimeout;
          DemandActionCombo.Text := DemandAction;
          
          HasReadConfig := True;
        end;

      end else begin
        IdleActionCombo.Text := 'stop';
        IdleTimeoutEdit.Text := '';
        DemandActionCombo.Text := 'start';
      end;
    end;
  end;
end;

function DuoStreamMonitorPrefs(Param: String): String;
begin
  Result := '???';
    
  if Param = 'IdleAction' then
  begin
    Result := DuoSettingsControls.IdleActionCombo.Text;
    if not (DuoSettingsControls.IdleTimeoutEdit.Text = '') then
      Result := Result + '+' + DuoSettingsControls.IdleTimeoutEdit.Text
  end;
  
  if Param = 'DemandAction' then
    Result := DuoSettingsControls.DemandActionCombo.Text;

end;
