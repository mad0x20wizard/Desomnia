[code]

type
  TEditServiceDialogControls = record
    ServiceNameLabel: TNewStaticText;
    NameEdit: TNewEdit;
    
    PortLabel: TNewStaticText;
    PortEdit: TNewEdit;
    
    ProtocolLabel: TNewStaticText;
    ProtocolCombo: TNewComboBox;

    OkButton: TNewButton;
    CancelButton: TNewButton;
  end;

var 
  EditServiceControls: TEditServiceDialogControls;

procedure ValidateEditService(ShowError: Boolean);
var
  Port: Integer;

begin
  with EditServiceControls do
  begin
    OkButton.ModalResult := 0;

    Port := StrToInt(PortEdit.Text);

    if NameEdit.Text = '' then
    begin
      if ShowError then
        ShowEditBalloon(NameEdit, 'Invalid Name', 'Name must not be empty.', TTI_ERROR);
      Exit;
    end;

    if Port = -1 then
    begin
      if ShowError then
        ShowEditBalloon(PortEdit, 'Invalid Port', 'Port must be numeric.', TTI_ERROR);
      Exit;
    end;

    if (Port < 0) or (Port > 65535) then
    begin
      if ShowError then
        ShowEditBalloon(PortEdit, 'Invalid Port', 'Please enter a number between 0 and 65535.', TTI_ERROR);
      Exit;
    end;

    OkButton.ModalResult := mrOk;
  end;
end;

procedure OnEditServiceChange(Sender: TObject);
begin
  ValidateEditService(False);
end;

procedure OnEditServiceOK(Sender: TObject);
begin
  ValidateEditService(True);
end;

function PromptService(Parent: TForm; var Service: TServiceConfig) : Boolean;
var
  Dialog: TSetupForm;
  Layout: TFormGridLayout;
  
begin
  Dialog := CreateCustomForm(ScaleX(225), ScaleY(200), False, True);
  Dialog.Caption := 'Add service';
  Dialog.BorderStyle := bsDialog;
  SetFormWidth(Dialog, ScaleX(225));

  Layout := CreateFormGridLayout(Dialog, ScaleX(80));

  with EditServiceControls do with Layout do
  begin

    NameEdit := TNewEdit.Create(Dialog);
    NameEdit.Parent := Dialog;
    NameEdit.Left := InputLeft;
    NameEdit.Top := Top;
    NameEdit.Width := InputWidth;
    NameEdit.Height := ComboBoxHeight;
    NameEdit.OnChange := @OnEditServiceChange;

    ServiceNameLabel := TNewStaticText.Create(Dialog);
    ServiceNameLabel.Parent := Dialog;
    ServiceNameLabel.AutoSize := True;
    ServiceNameLabel.Caption := 'Name:';
    ServiceNameLabel.Left := InputLeft - ServiceNameLabel.Width - ColumnGap;
    ServiceNameLabel.Top := CenterLeftBetweenRightY(ServiceNameLabel, NameEdit);
    
    Top := Top + NameEdit.Height + RowGap
    
    PortEdit := TNewEdit.Create(Dialog);
    PortLabel := TNewStaticText.Create(Dialog);

    PortEdit.Parent := Dialog;
    PortEdit.Left := InputLeft;
    PortEdit.Top := Top;
    PortEdit.Width := InputWidth;
    PortEdit.Height := NameEdit.Height;
    PortEdit.MaxLength := 5;
    PortEdit.OnChange := @OnEditServiceChange;
    
    PortLabel.Parent := Dialog;
    PortLabel.AutoSize := True;
    PortLabel.Caption := 'Port:';
    PortLabel.Left := InputLeft - PortLabel.Width - ColumnGap;
    PortLabel.Top := CenterLeftBetweenRightY(PortLabel, PortEdit);

    Top := Top + PortEdit.Height + RowGap
    
    ProtocolCombo := TNewComboBox.Create(Dialog);
    ProtocolCombo.Parent := Dialog;
    ProtocolCombo.Left := InputLeft;
    ProtocolCombo.Top := Top;
    ProtocolCombo.Width := InputWidth;
    ProtocolCombo.Height := ScaleY(ProtocolCombo.Height)
    ProtocolCombo.Style := csDropDownList;
    ProtocolCombo.Items.Add('TCP');
    ProtocolCombo.Items.Add('UDP');
    ProtocolCombo.ItemIndex := 0;
    
    ProtocolLabel := TNewStaticText.Create(Dialog);
    ProtocolLabel.Parent := Dialog;
    ProtocolLabel.AutoSize := True;
    ProtocolLabel.Caption := 'Protocol:';
    ProtocolLabel.Left := InputLeft - ProtocolLabel.Width - ColumnGap;
    ProtocolLabel.Top := CenterLeftBetweenRightY(ProtocolLabel, ProtocolCombo);

    Top := Top + ProtocolCombo.Height + RowGap

    OkButton := TNewButton.Create(Dialog);
    OkButton.Parent := Dialog;
    OkButton.Caption := 'OK';
    OKButton.OnClick := @OnEditServiceOK;
    OkButton.Default := True;

    CancelButton := TNewButton.Create(Dialog);
    CancelButton.Parent := Dialog;
    CancelButton.Caption := 'Cancel';
    CancelButton.ModalResult := mrCancel;
    CancelButton.Cancel := True;
    
    LayoutFormButtons(Layout, [CancelButton, OKButton]);

    SetFormHeight(Dialog, Top + ButtonHeight + PaddingBottom);
  end;
  
  try
    Dialog.FlipAndCenterIfNeeded(True, Parent, False);
    
    if Dialog.ShowModal() = mrOk then with EditServiceControls do
    begin
      Service.Name := NameEdit.Text;
      Service.Port := PortEdit.Text;
      Service.Protocol := ProtocolCombo.Text;
      Result := True;
    end
    else
      Result := False;
  finally
    Dialog.Free();
  end;
end;