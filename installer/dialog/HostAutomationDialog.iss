[code]

const
  AutomationDialogWidth = 750;
  AutomationDialogLabelWidth = 50;
  AutomationDialogRadioIndent = 20;

type
  TAutomationDialogControls = record
    HostLabel: TNewStaticText;
    HostCombo: TNewComboBox;

    AutoStartMagicPacketCheckbox: TNewCheckBox;
    AutoStartCheckbox: TNewCheckBox;
    AutoIdleCheckbox: TNewCheckBox;
    AutoIdleDelayLabel: TNewStaticText;
    AutoIdleDelayCombo: TNewComboBox;

    AutoSuspendRadio: TNewRadioButton;
    AutoStopRadio: TNewRadioButton;

    OkButton: TNewButton;
    CancelButton: TNewButton;

    Rebuilding: Boolean;
  end;

var 
  ConfigureAutomationDialog: TSetupForm;
  ConfigureAutomationControls: TAutomationDialogControls;
  ConfigureAutomationConfig: THostServiceConfig;

procedure SeTHostAutomationConfigControlsEnabled(Enabled: Boolean);
begin
  with ConfigureAutomationControls do
  begin
    AutoStartMagicPacketCheckbox.Enabled := Enabled;
    AutoStartCheckbox.Enabled := Enabled;
    AutoIdleCheckbox.Enabled := Enabled;
    AutoIdleDelayLabel.Enabled := Enabled;
    AutoIdleDelayCombo.Enabled := Enabled;
    AutoSuspendRadio.Enabled := Enabled;
    AutoStopRadio.Enabled := Enabled;
  end;
end;

procedure OnHostAutomationChanged(Sender: TObject);
var
  Automation: THostAutomationConfig;
  Idle: String;
begin

  with ConfigureAutomationConfig do with ConfigureAutomationControls do if (Index > -1) and not Rebuilding then
  begin
    Automation := Hosts[Index].Automation;

    if AutoStartMagicPacketCheckbox.Enabled then
      if AutoStartMagicPacketCheckbox.Checked then
        Automation.MagicPacket := 'start'
      else
        Automation.MagicPacket := '';

    if AutoStartCheckbox.Enabled then
      if AutoStartCheckbox.Checked then
        Automation.Demand := 'start'
      else
        Automation.Demand := '';

    Idle := '';


    if AutoIdleCheckbox.Enabled and AutoIdleCheckbox.Checked then
    begin
      AutoIdleDelayLabel.Enabled := True;
      AutoIdleDelayCombo.Enabled := True;
      AutoSuspendRadio.Enabled := True;
      AutoStopRadio.Enabled := True;

      if (not AutoSuspendRadio.Checked) and (not AutoStopRadio.Checked) then
        AutoSuspendRadio.Checked := True;

      if AutoSuspendRadio.Checked then
        Idle := 'suspend';
      if AutoStopRadio.Checked then
        Idle := 'stop';

      if AutoIdleDelayCombo.Enabled and (AutoIdleDelayCombo.Text <> '') then
        Idle := Idle + '+' + AutoIdleDelayCombo.Text;
    end
    else
    begin
      AutoIdleDelayLabel.Enabled := False;
      AutoIdleDelayCombo.Enabled := False;
      AutoSuspendRadio.Checked := False;
      AutoStopRadio.Checked := False;
      AutoSuspendRadio.Enabled := False;
      AutoStopRadio.Enabled := False;
    end;

    Automation.Idle := Idle;

    Hosts[Index].Automation := Automation;
  end;
end;

procedure UpdateHostAutomationForm(Config: THostServiceConfig);
var
  I: Integer;
  Host: THostConfig;
  Idle: array of String;
begin
  with ConfigureAutomationControls do
  begin
    Rebuilding := True;

    HostCombo.Text := '';
    HostCombo.Items.Clear();
    HostCombo.Enabled := False;
    HostCombo.ItemIndex := -1;

    //SeTHostAutomationConfigControlsEnabled(False);

    for I := 0 to GetArrayLength(Config.Hosts) - 1 do
    begin      
      HostCombo.Items.Add(Config.Hosts[I].Name);
      HostCombo.Enabled := True;
    end;
    
    if GetArrayLength(Config.Hosts) > Config.Index then
    begin
      HostCombo.ItemIndex := Config.Index;
    end;
      
    if HostCombo.ItemIndex > -1 then
    begin
      Host := Config.Hosts[HostCombo.ItemIndex];

      AutoStartMagicPacketCheckbox.Checked := Host.Automation.MagicPacket = 'start';
      AutoStartCheckbox.Checked := Host.Automation.Demand = 'start';

      Idle := StringSplit(Host.Automation.Idle, ['+'], stExcludeEmpty);

      if (GetArrayLength(Idle) > 1) and (Idle[1] <> '') then
        AutoIdleDelayCombo.Text := Idle[1]
      else
        AutoIdleDelayCombo.Text := '';

      if (GetArrayLength(Idle) > 0) then
      begin
        AutoIdleCheckbox.Checked := Idle[0] <> '';
        AutoSuspendRadio.Checked := Idle[0] = 'suspend';
        AutoStopRadio.Checked := Idle[0] = 'stop';
      end
      else
      begin
        AutoIdleCheckbox.Checked := False;
        AutoSuspendRadio.Checked := False;
        AutoStopRadio.Checked := False;
      end;

      SeTHostAutomationConfigControlsEnabled(True);
    end
    else
    begin
      AutoStartMagicPacketCheckbox.Checked := False;
      AutoStartCheckbox.Checked := False;
      AutoIdleCheckbox.Checked := False;
      AutoSuspendRadio.Checked := False;
      AutoStopRadio.Checked := False;
      AutoIdleDelayCombo.Text := '';

      SeTHostAutomationConfigControlsEnabled(False);
    end;

    Rebuilding := False;

    OnHostAutomationChanged(nil);
  end;
end;

procedure OnHostAutomationIndexChanged(Sender: TObject);
begin
  with ConfigureAutomationConfig do with ConfigureAutomationControls do if not Rebuilding then
  begin
    Index := HostCombo.ItemIndex;

    UpdateHostAutomationForm(ConfigureAutomationConfig);
  end;
end;




function CreateDelayComboBox(Layout: TFormGridLayout; BoxLabel: TNewStaticText; Immediate: Boolean): TNewComboBox;
begin
  with Layout do
  begin
    Result := TNewComboBox.Create(Layout.Form);
    Result.Parent := Layout.Form;
    Result.Top := Top;
    Result.Width := ScaleX(DurationComboWidth);
    Result.Left := InputRight - Result.Width;
    
    if Immediate Then
      Result.Items.Add('immediate');
    
    Result.Items.Add('30 s');
    Result.Items.Add('5 min');
    Result.Items.Add('30 min');
    Result.Items.Add('1 h');
    
    BoxLabel.Parent := Form;
    BoxLabel.WordWrap := False;
    BoxLabel.AutoSize := True;
    BoxLabel.Left := InputRight - Result.Width - BoxLabel.Width - RowGap;
    BoxLabel.Top := Result.Top + (Result.Height - BoxLabel.Height) / 2;
  end;
end;

function CreateConfigureAutomationDialog(): TSetupForm;
var
  Layout: TFormGridLayout;

begin
  Result := CreateCustomForm(AutomationDialogWidth, ScaleY(200), False, True);
  Result.BorderStyle := bsDialog;
  SetFormWidth(Result, AutomationDialogWidth);

  Layout := CreateFormGridLayout(Result, ScaleX(AutomationDialogLabelWidth));

  with ConfigureAutomationControls do with Layout do
  begin
    Rebuilding := True;

    HostCombo := TNewComboBox.Create(Form);
    HostCombo.Parent := Form;
    HostCombo.Left := InputLeft;
    HostCombo.Top := Top;
    HostCombo.Width := InputWidth;
    HostCombo.Height := ScaleY(HostCombo.Height)
    HostCombo.Style := csDropDownList;
    HostCombo.Items.Add('Dummy');
    HostCombo.OnChange := @OnHostAutomationIndexChanged;

    ComboBoxHeight := HostCombo.Height;

    // HostCombo.OnChange := @OnAutomationHostChange;
    // HostCombo.OnChange := @OnHostIndexChange;
    // HostCombo.OnKeyDown := @OnHostKeyDown;
    // HostCombo.OnKeyUp := @OnHostKeyUp;

    ButtonHeight := ComboBoxHeight;

    HostLabel := AddControlLabel(Layout, HostCombo, 'Host:');

    Top := Top + HostCombo.Height + RowGap

    AddBevelLine(Layout);

    AutoStartMagicPacketCheckbox := TNewCheckBox.Create(Form);
    AutoStartMagicPacketCheckbox.Parent := Form;
    AutoStartMagicPacketCheckbox.Left := InputLeft;      // indent under the radio
    AutoStartMagicPacketCheckbox.Top := Top;
    AutoStartMagicPacketCheckbox.Width := InputWidth;
    AutoStartMagicPacketCheckbox.Height := ScaleY(AutoStartMagicPacketCheckbox.Height);
    AutoStartMagicPacketCheckbox.Caption := 'Start on Magic Packet (Wake-on-LAN)';
    AutoStartMagicPacketCheckbox.OnClick := @OnHostAutomationChanged;

    Top := Top + AutoStartMagicPacketCheckbox.Height + RowGap;

    AutoStartCheckbox := TNewCheckBox.Create(Form);
    AutoStartCheckbox.Parent := Form;
    AutoStartCheckbox.Left := InputLeft;      // indent under the radio
    AutoStartCheckbox.Top := Top;
    AutoStartCheckbox.Width := InputWidth;
    AutoStartCheckbox.Height := ScaleY(AutoStartCheckbox.Height);
    AutoStartCheckbox.Caption := 'Start on service demand';
    AutoStartCheckbox.OnClick := @OnHostAutomationChanged;

    Top := Top + AutoStartCheckbox.Height + RowGap;

    AutoIdleCheckbox := TNewCheckBox.Create(Form);
    AutoIdleCheckbox.Parent := Form;
    AutoIdleCheckbox.Left := InputLeft;
    AutoIdleCheckbox.Top := Top;
    AutoIdleCheckbox.Width := InputWidth;
    AutoIdleCheckbox.Height := ScaleY(AutoIdleCheckbox.Height);
    AutoIdleCheckbox.Caption := 'When idle';
    AutoIdleCheckbox.OnClick := @OnHostAutomationChanged;

    AutoIdleDelayLabel := TNewStaticText.Create(Form);
    AutoIdleDelayLabel.Caption := 'with delay';
    AutoIdleDelayCombo := CreateDelayComboBox(Layout, AutoIdleDelayLabel, False);
    AutoIdleDelayCombo.Text := '';
    AutoIdleDelayCombo.OnChange := @OnHostAutomationChanged;

    Top := Top + AutoIdleCheckbox.Height + RowGap;

    AutoSuspendRadio := TNewRadioButton.Create(Form);
    AutoSuspendRadio.Parent := Form;
    AutoSuspendRadio.Left := InputLeft + ScaleX(AutomationDialogRadioIndent);
    AutoSuspendRadio.Top := Top;
    AutoSuspendRadio.Width := InputWidth - ScaleX(AutomationDialogRadioIndent);
    AutoSuspendRadio.Height := ScaleY(AutoSuspendRadio.Height);
    AutoSuspendRadio.Caption := 'Suspend';
    AutoSuspendRadio.OnClick := @OnHostAutomationChanged;

    Top := Top + AutoSuspendRadio.Height + RowGap;

    AutoStopRadio := TNewRadioButton.Create(Form);
    AutoStopRadio.Parent := Form;
    AutoStopRadio.Left := InputLeft + ScaleX(AutomationDialogRadioIndent);
    AutoStopRadio.Top := Top;
    AutoStopRadio.Width := InputWidth - ScaleX(AutomationDialogRadioIndent);
    AutoStopRadio.Height := ScaleY(AutoStopRadio.Height);
    AutoStopRadio.Caption := 'Stop';
    AutoStopRadio.OnClick := @OnHostAutomationChanged;

    Top := Top + AutoStopRadio.Height + RowGap;

    AddBevelLine(Layout);

    OkButton := TNewButton.Create(Form);
    OkButton.Caption := 'OK';
    // OKButton.OnClick := @OnSecurityOK;
    OkButton.ModalResult := mrOk;
    OkButton.Default := True;

    CancelButton := TNewButton.Create(Form);
    CancelButton.Caption := 'Cancel';
    CancelButton.ModalResult := mrCancel;
    CancelButton.Cancel := True;
    
    LayoutFormButtons(Layout, [CancelButton, OKButton]);

    SetFormHeight(Form, Top + ButtonHeight + PaddingBottom);

    Rebuilding := False;
  end;
end;



function ConfigureHostAutomation(Config: THostServiceConfig) : THostServiceConfig;
var
  ConfigCopy: THostServiceConfig;
begin
  ConfigureAutomationDialog := CreateConfigureAutomationDialog();
  ConfigureAutomationDialog.FlipAndCenterIfNeeded(True, WizardForm, False);
  ConfigureAutomationDialog.Caption := 'Configure virtual host automation';

  ConfigCopy := CopyHostServiceConfig(Config); ConfigureAutomationConfig := ConfigCopy;
  
  SanitizeHostIndex(ConfigureAutomationConfig);

  UpdateHostAutomationForm(ConfigureAutomationConfig);
  
  try
    if ConfigureAutomationDialog.ShowModal = mrOk then
      Result := ConfigureAutomationConfig
    else
      Result := Config;
  finally
    ConfigureAutomationDialog.Free();
  end;
end;