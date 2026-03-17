[Code]

const
  HostServiceDialogWidth = 300;
  HostServiceDialogMediumWidth = 350;
  HostServiceDialogWideWidth = 400;
  HostServiceDialogLabelWidth = 80;

function CreateConfigureServicesDialog(DialogStyle: THostServiceDialogStyle) : TSetupForm;
var
  DialogClientWidth: Integer;

  Dialog: TSetupForm;
  Layout: TFormGridLayout;

begin
  DialogClientWidth := ScaleX(HostServiceDialogWidth);
  if (DialogStyle and hsdsHostEdit) <> 0 then
    DialogClientWidth := ScaleX(HostServiceDialogWideWidth)
  else if (DialogStyle and (hsdsMacEdit or hsdsIPEdit)) <> 0 then
    DialogClientWidth := ScaleX(HostServiceDialogMediumWidth);

  Dialog := CreateCustomForm(DialogClientWidth, ScaleY(200), False, True);
  Dialog.BorderStyle := bsDialog;
  SetFormWidth(Dialog, DialogClientWidth);
  Result := Dialog;

  Layout := CreateFormGridLayout(Dialog, ScaleX(HostServiceDialogLabelWidth));
  
  with ConfigureServicesControls do with Layout do
  begin
    Style := DialogStyle;

    HostCombo := TNewComboBox.Create(Form);
    HostCombo.Parent := Form;
    HostCombo.Width := InputWidth;
    HostCombo.Height := ScaleY(HostCombo.Height)
    HostCombo.Left := InputLeft;
    HostCombo.Top := Top;
    HostCombo.Style := csDropDownList;
    HostCombo.Items.Add('Dummy');

    ComboBoxHeight := HostCombo.Height;

    HostCombo.OnChange := @OnHostIndexChange;
    HostCombo.OnKeyDown := @OnHostKeyDown;
    HostCombo.OnKeyUp := @OnHostKeyUp;

    ButtonHeight := ComboBoxHeight;

    HostCombo.Visible := False;
    HostLabel := AddControlLabel(Layout, HostCombo, 'Host:');
    HostLabel.Visible := False;

    if (Style and hsdsHost) <> 0 then
    begin
      HostLabel.Visible := True;
      HostCombo.Visible := True;
    end;

    HostAddButton := TNewButton.Create(Form);
    HostRemoveButton := TNewButton.Create(Form);
    HostRemoveButton.Caption := 'Remove';
    HostAddButton.Caption := 'Add';

    LayoutFormButtons(Layout, [HostAddButton, HostRemoveButton]);

    if (Style and hsdsHostEdit) <> 0 then
    begin
      HostCombo.Width := HostCombo.Width - HostAddButton.Width - HostRemoveButton.Width - ColumnGap * 2;
      HostRemoveButton.OnClick := @OnHostRemove;
      HostAddButton.OnClick := @OnHostAdd;
    end
    else
    begin
      HostRemoveButton.Visible := False;
      HostAddButton.Visible := False;
    end;
    
    if HostCombo.Visible then
    begin
      Top := Top + HostCombo.Height + RowGap
      
      BevelTop := TBevel.Create(Form);
      BevelTop.Top := Top;
      BevelTop.Left := -1;
      BevelTop.Width := Form.Width;
      BevelTop.Height := 2;
      BevelTop.Parent := Form;

      Top := Top + RowGap
    end;
    
    MacEdit := TNewEdit.Create(Form);
    MacLabel := TNewStaticText.Create(Form);
    MacAutoCheckbox := TNewCheckBox.Create(Form);
    MacHelpLabel := TNewStaticText.Create(Form);
    IPLabel := TNewStaticText.Create(Form);
    IPAutoCheckbox := TNewCheckBox.Create(Form);

    if (Style and hsdsHostEdit) <> 0 then
    begin
      HostCombo.Style := csDropDown;
    end;

    if (Style and (hsdsMacEdit or hsdsIPEdit)) <> 0 then
    begin

      MacAutoCheckbox.Width := HostAddButton.Width;
      MacAutoCheckbox.Left := HostAddButton.Left;

      if (Style and hsdsMacEdit) <> 0 then
      begin
        MacEdit.Parent := Form;
        MacEdit.Left := InputLeft;
        MacEdit.Top := Top;
        MacEdit.Width := InputWidth;
        MacEdit.OnChange := @OnMacAddressChange;
        
        MacLabel := AddControlLabel(Layout, MacEdit, 'MAC address:');

        MacAutoCheckbox.Parent := Form;
        MacAutoCheckbox.Top := Top + ScaleY(2);
        MacAutoCheckbox.Height := ScaleY(MacAutoCheckbox.Height);
        MacAutoCheckbox.Caption := 'Automatic';
        MacAutoCheckbox.OnClick := @OnMacAddressAutoChange;
        
        MacEdit.Width := MacEdit.Width - MacAutoCheckbox.Width - ColumnGap;

        Top := Top + MacEdit.Height + RowGap / 2;
        
        MacHelpLabel.Parent := Form;
        MacHelpLabel.Left := InputLeft;
        MacHelpLabel.Top := Top;
        MacHelpLabel.Width := InputWidth;
        MacHelpLabel.AutoSize := True;
        MacHelpLabel.WordWrap := True;
        MacHelpLabel.Caption :=
          'To automatically wake up a remote host on access, you must provide its physical address.  ' +
          'You can let Desomnia try to resolve this automatically, but the result may not be very deterministic.';

        Top := Top + MacHelpLabel.Height + RowGap;
      end;

      if (Style and hsdsIPEdit) <> 0 then
      begin
        IPAutoCheckbox.Parent := Form;
        IPAutoCheckbox.Width := MacAutoCheckbox.Width;
        IPAutoCheckbox.Left := MacAutoCheckbox.Left;
        IPAutoCheckbox.Top := Top + ScaleY(2);
        IPAutoCheckbox.Height := ScaleY(IPAutoCheckbox.Height);
        IPAutoCheckbox.Caption := 'Automatic';
        IPAutoCheckbox.OnClick := @OnIPAddressAutoChange;

        IPInput := CreateIPAddressInput(Form, InputLeft, Top, MacEdit.Height, InputWidth - MacAutoCheckbox.Width - ColumnGap);
        
        IPLabel.Parent := Form;
        IPLabel.AutoSize := True;
        IPLabel.Caption := 'IP address:';
        IPLabel.Left := InputLeft - IPLabel.Width - ColumnGap;
        IPLabel.Top := Top + (MacEdit.Height - IPLabel.Height) / 2;


        Top := Top + MacEdit.Height + RowGap;
      end;

      AddBevelLine(Layout);
    end;

    ServicesList := TNewCheckListBox.Create(Form);
    ServicesList.Parent := Form;
    ServicesList.Left := InputLeft;
    ServicesList.Top := Top;
    ServicesList.Width := InputWidth;
    ServicesList.Height := ScaleY(200);
    ServicesList.Flat := False;
    
    ServicesLabel := AddControlLabel(Layout, ServicesList, 'Services:');

    ServiceRemoveButton := TNewButton.Create(Form);
    ServiceRemoveButton.Parent := Form;
    ServiceRemoveButton.Caption := 'Remove';
    ServiceRemoveButton.Left := PaddingLeft;
    ServiceRemoveButton.Width := LabelWidth - ColumnGap;
    ServiceRemoveButton.Height := HostCombo.Height;
    ServiceRemoveButton.Top := ServicesList.Top + ServicesList.Height - ServiceRemoveButton.Height;
    ServiceRemoveButton.OnClick := @OnServiceRemove;

    ServiceAddButton := TNewButton.Create(Form);
    ServiceAddButton.Parent := Form;
    ServiceAddButton.Caption := 'Add';
    ServiceAddButton.Left := PaddingLeft;
    ServiceAddButton.Width := LabelWidth - ColumnGap;
    ServiceAddButton.Height := HostCombo.Height;
    ServiceAddButton.Top := ServiceRemoveButton.Top - ServiceAddButton.Height - ColumnGap;
    ServiceAddButton.OnClick := @OnServiceAdd;

    Top := Top + ServicesList.Height + RowGap / 2

    ServicesHelpLabel := TNewStaticText.Create(Form);
    ServicesHelpLabel.Parent := Form;
    ServicesHelpLabel.Left := InputLeft;
    ServicesHelpLabel.Top := Top;
    ServicesHelpLabel.Width := InputWidth;
    ServicesHelpLabel.AutoSize := True;
    ServicesHelpLabel.WordWrap := True;
    ServicesHelpLabel.Caption :=
      'Desomnia will only monitor traffic for the configured services. ';
      
    Top := Top + ServicesHelpLabel.Height + RowGap;

    AddBevelLine(Layout);

    OkButton := TNewButton.Create(Form);
    OkButton.Parent := Form;
    OkButton.Caption := 'OK';
    OKButton.ModalResult := mrOk;
    OKButton.OnClick := @OnHostServiceOK;
    OkButton.Default := True;

    CancelButton := TNewButton.Create(Form);
    CancelButton.Parent := Form;
    CancelButton.Caption := 'Cancel';
    CancelButton.ModalResult := mrCancel;
    CancelButton.Cancel := True;
    
    SecurityButton := TNewButton.Create(Form);
    SecurityIcon := TBitmapImage.Create(Form);

    if (Style and hsdsSecurity) <> 0 then
    begin
      SecurityButton.Parent := Form;
      SecurityButton.Caption := 'Security';
      SecurityButton.Top := Top;
      SecurityButton.Left := PaddingLeft;
      SecurityButton.Width := LabelWidth - ColumnGap;
      SecurityButton.Height := HostCombo.Height;
      SecurityButton.OnClick := @OnSecurityClick;

      SecurityIcon.Parent := Form;
      SecurityIcon.Width := ScaleX(16);
      SecurityIcon.Height := ScaleY(16);
      SecurityIcon.Left := InputLeft;
      SecurityIcon.Top := CenterLeftBetweenRightY(SecurityIcon, SecurityButton);
      SecurityIcon.Enabled := False;

      InitializeBitmapImageFromStockIcon(SecurityIcon, SIID_LOCK, clNone, []);
    end;
    
    LayoutFormButtons(Layout, [CancelButton, OKButton]);
    
    SetFormHeight(Form, Top + ButtonHeight + PaddingBottom);
  end;
end;

procedure SetHostControlsEnabled(Enabled: Boolean);
begin
  with ConfigureServicesControls do
  begin
    MacLabel.Enabled := Enabled;
    MacEdit.Enabled := Enabled;
    MacAutoCheckbox.Enabled := Enabled;
    MacHelpLabel.Enabled := Enabled;
    IPLabel.Enabled := Enabled;
    EnableWindow(IPInput, Enabled);
    IPAutoCheckbox.Enabled := Enabled;

    ServicesLabel.Enabled := Enabled;
    ServicesList.Enabled := Enabled;
    ServiceAddButton.Enabled := Enabled;
    ServiceRemoveButton.Enabled := Enabled;
    ServicesHelpLabel.Enabled := Enabled;
    SecurityButton.Enabled := Enabled;
  end;
end;
