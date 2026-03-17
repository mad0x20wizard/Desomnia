[code]

const
  SecurityDialogWidth = 300;
  SecurityDialogLabelWidth = 80;

type
  TSecurityDialogControls = record
    MethodLabel: TNewStaticText;
    MethodCombo: TNewComboBox;
    
    PortLabel: TNewStaticText;
    PortEdit: TNewEdit;
    ProtocolLabel: TNewStaticText;
    ProtocolCombo: TNewComboBox;

    EncodingLabel: TNewStaticText;
    EncodingCombo: TNewComboBox;

    SecretLabel: TNewStaticText;
    SecretMemo: TMemo;
    SecretIcon: TBitmapImage;

    SecretAuthLabel: TNewStaticText;
    SecretAuthMemo: TMemo;
    SecretAuthIcon: TBitmapImage;

    SecretAuthTypeLabel: TNewStaticText;
    SecretAuthTypeCombo: TNewComboBox;

    OkButton: TNewButton;
    CancelButton: TNewButton;

    Rebuilding: Boolean;
  end;

var 
  //SecurityDialog: TSetupForm;
  SecurityControls: TSecurityDialogControls;

procedure ValidateSecurity(ShowError: Boolean);
var
  Port: Integer;

begin
  with SecurityControls do if not Rebuilding then
  begin
    OkButton.ModalResult := 0;

    if PortEdit.Enabled then
    begin
      Port := StrToInt(PortEdit.Text);

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
    end;

    if SecretMemo.Enabled then
    begin
      if SecretMemo.Text = '' then
      begin
        if ShowError then
          ShowEditBalloon(SecretMemo, 'Invalid Key', 'Secret must not be empty.', TTI_ERROR);
        Exit;
      end;
    end;

    // if SecretAuthMemo.Enabled then
    // begin
    //   if SecretAuthMemo.Text = '' then
    //   begin
    //     if ShowError then
    //       ShowEditBalloon(SecretAuthMemo, 'Invalid Key', 'Secret Auth must not be empty.', TTI_ERROR);
    //     Exit;
    //   end;
    // end;


    OkButton.ModalResult := mrOk;
  end;
end;

procedure OnSecurityMethodChange(Sender: TObject);
begin
  with SecurityControls do
  begin
    PortLabel.Enabled := False;
    PortEdit.Enabled := False;
    ProtocolLabel.Enabled := False;
    ProtocolCombo.Enabled := False;
    EncodingLabel.Enabled := False;
    EncodingCombo.Enabled := False;
    SecretLabel.Enabled := False;
    SecretMemo.Enabled := False;
    SecretIcon.Visible := False;
    SecretAuthLabel.Enabled := False;
    SecretAuthMemo.Enabled := False;
    SecretAuthIcon.Visible := False;
    SecretAuthTypeLabel.Enabled := False;
    SecretAuthTypeCombo.Enabled := False;

    if MethodCombo.ItemIndex > 0 then
    begin
      PortLabel.Enabled := True;
      PortEdit.Enabled := True;
      ProtocolLabel.Enabled := True;
      //ProtocolCombo.Enabled := True; // TODO: Enable when TCP works
      EncodingLabel.Enabled := True;
      EncodingCombo.Enabled := True; 
      SecretLabel.Enabled := True;
      SecretMemo.Enabled := True;
      SecretIcon.Visible := True;

      if not (Sender = nil) then
        EncodingCombo.ItemIndex := 0;
    end;

    if MethodCombo.ItemIndex > 1 then
    begin
      SecretAuthLabel.Enabled := True;
      SecretAuthMemo.Enabled := True;
      SecretAuthIcon.Visible := True;
      SecretAuthTypeLabel.Enabled := True;
      SecretAuthTypeCombo.Enabled := True;

      if not (Sender = nil) then
        EncodingCombo.ItemIndex := 1;
    end;
  end;

  ValidateSecurity(False);
end;

procedure OnSecurityChange(Sender: TObject);
begin
  ValidateSecurity(False);
end;

procedure OnSecurityOK(Sender: TObject);
begin
  ValidateSecurity(True);
end;

function CreateSecurityControls(Dialog: TSetupForm) : TSecurityDialogControls;
var
  Layout: TFormGridLayout;

begin
  SetFormWidth(Dialog, ScaleX(SecurityDialogWidth));

  Layout := CreateFormGridLayout(Dialog, ScaleX(SecurityDialogLabelWidth));

  with Result do with Layout do
  begin
    Rebuilding := True;

    MethodCombo := TNewComboBox.Create(Dialog);
    MethodCombo.Parent := Dialog;
    MethodCombo.Left := InputLeft;
    MethodCombo.Top := Top;
    MethodCombo.Width := InputWidth;
    MethodCombo.Height := ScaleY(MethodCombo.Height)
    MethodCombo.Style := csDropDownList;
    MethodCombo.Items.Add('None');
    MethodCombo.Items.Add('Plain Text');
    if IsComponentSelected('plugins\FirewallKnockOperator') then
      MethodCombo.Items.Add('Firewall Knock Operator');
    MethodCombo.ItemIndex := 0;

    MethodCombo.OnChange := @OnSecurityMethodChange;

    MethodLabel := AddControlLabel(Layout, MethodCombo, 'Method:');

    Top := Top + MethodCombo.Height + RowGap

    AddBevelLine(Layout);
    
    PortEdit := TNewEdit.Create(Dialog);
    PortLabel := TNewStaticText.Create(Dialog);

    PortEdit.Parent := Dialog;
    PortEdit.Left := InputLeft;
    PortEdit.Top := Top;
    PortEdit.Width := InputWidth;
    PortEdit.Height := ButtonHeight;
    PortEdit.MaxLength := 5;
    PortEdit.OnChange := @OnSecurityChange;
    PortEdit.Text := '62201';
    
    PortLabel := AddControlLabel(Layout, PortEdit, 'Port:');

    Top := Top + PortEdit.Height + RowGap
    
    ProtocolCombo := TNewComboBox.Create(Dialog);
    ProtocolCombo.Parent := Dialog;
    ProtocolCombo.Left := InputLeft;
    ProtocolCombo.Top := Top;
    ProtocolCombo.Width := InputWidth;
    ProtocolCombo.Height := ScaleY(ProtocolCombo.Height)
    ProtocolCombo.Style := csDropDownList;
    ProtocolCombo.Items.Add('UDP');
    ProtocolCombo.Items.Add('TCP');
    ProtocolCombo.ItemIndex := 0;
    
    ProtocolLabel := AddControlLabel(Layout, ProtocolCombo, 'Protocol:');

    Top := Top + ProtocolCombo.Height + RowGap

    AddBevelLine(Layout);

    EncodingCombo := TNewComboBox.Create(Dialog);
    EncodingCombo.Parent := Dialog;
    EncodingCombo.Left := InputLeft;
    EncodingCombo.Top := Top;
    EncodingCombo.Width := InputWidth;
    EncodingCombo.Height := ScaleY(EncodingCombo.Height)
    EncodingCombo.Style := csDropDownList;
    EncodingCombo.Items.Add('UTF-8');
    EncodingCombo.Items.Add('Base64');
    EncodingCombo.ItemIndex := 0;

    EncodingLabel := AddControlLabel(Layout, EncodingCombo, 'Encoding:');

    Top := Top + EncodingCombo.Height + RowGap

    SecretMemo := TMemo.Create(Dialog);
    SecretMemo.Parent := Dialog;
    SecretMemo.Left := InputLeft;
    SecretMemo.Top := Top;
    SecretMemo.Width := InputWidth;
    SecretMemo.Height := ScaleY(60);
    SecretMemo.OnChange := @OnSecurityChange;

    SecretLabel := AddControlLabel(Layout, SecretMemo, 'Secret:');

    SecretIcon := TBitmapImage.Create(Dialog);
    SecretIcon.Parent := Dialog;
    SecretIcon.Width := ScaleX(16);
    SecretIcon.Height := ScaleY(16);
    SecretIcon.Left := SecretLabel.Left - SecretIcon.Width;
    SecretIcon.Top := Top + ScaleY(2);

    InitializeBitmapImageFromStockIcon(SecretIcon, SIID_KEY, clNone, []);

    Top := Top + SecretMemo.Height + RowGap

    SecretAuthMemo := TMemo.Create(Dialog);
    SecretAuthMemo.Parent := Dialog;
    SecretAuthMemo.Left := InputLeft;
    SecretAuthMemo.Top := Top;
    SecretAuthMemo.Width := InputWidth;
    SecretAuthMemo.Height := ScaleY(100);
    SecretAuthMemo.OnChange := @OnSecurityChange;

    SecretAuthLabel := AddControlLabel(Layout, SecretAuthMemo, 'Auth:');

    SecretAuthIcon := TBitmapImage.Create(Dialog);
    SecretAuthIcon.Parent := Dialog;
    SecretAuthIcon.Width := ScaleX(16);
    SecretAuthIcon.Height := ScaleY(16);
    SecretAuthIcon.Left := SecretAuthLabel.Left - SecretAuthIcon.Width;
    SecretAuthIcon.Top := Top + ScaleY(2);

    InitializeBitmapImageFromStockIcon(SecretAuthIcon, SIID_KEY, clNone, []);

    Top := Top + SecretAuthMemo.Height + RowGap

    SecretAuthTypeCombo := TNewComboBox.Create(Dialog);
    SecretAuthTypeCombo.Parent := Dialog;
    SecretAuthTypeCombo.Left := InputLeft;
    SecretAuthTypeCombo.Top := Top;
    SecretAuthTypeCombo.Width := InputWidth;
    SecretAuthTypeCombo.Height := ScaleY(SecretAuthTypeCombo.Height)
    SecretAuthTypeCombo.Style := csDropDownList;
    SecretAuthTypeCombo.Items.Add('Default');
    SecretAuthTypeCombo.Items.Add('MD5');
    SecretAuthTypeCombo.Items.Add('SHA1');
    SecretAuthTypeCombo.Items.Add('SHA256');
    SecretAuthTypeCombo.Items.Add('SHA384');
    SecretAuthTypeCombo.Items.Add('SHA512');
    SecretAuthTypeCombo.Items.Add('SHA3_256');
    SecretAuthTypeCombo.Items.Add('SHA3_512');
    SecretAuthTypeCombo.ItemIndex := 0;

    SecretAuthTypeLabel := AddControlLabel(Layout, SecretAuthTypeCombo, 'Digest Type:');

    Top := Top + SecretAuthTypeCombo.Height + RowGap

    AddBevelLine(Layout);

    OkButton := TNewButton.Create(Dialog);
    OkButton.Caption := 'OK';
    OKButton.OnClick := @OnSecurityOK;
    OkButton.Default := True;

    CancelButton := TNewButton.Create(Dialog);
    CancelButton.Caption := 'Cancel';
    CancelButton.ModalResult := mrCancel;
    CancelButton.Cancel := True;
    
    LayoutFormButtons(Layout, [CancelButton, OKButton]);

    SetFormHeight(Dialog, Top + ButtonHeight + PaddingBottom);

    Rebuilding := False;
  end;
end;

procedure UpdateSecurityControls(Controls: TSecurityDialogControls; Security: TSecurityConfig);
begin
  with Controls do
  begin
    MethodCombo.Text := Security.Method;

    case Security.Method of
      '':
        MethodCombo.ItemIndex := 0;
      'plain':
        MethodCombo.ItemIndex := 1;
      'fko':
        MethodCombo.ItemIndex := 2;
    else
      MsgBox('Unknown method.', mbError, MB_OK);
    end;

    if Security.Port <> '' then
      PortEdit.Text := Security.Port;

    case Security.Protocol of
      '':
        ProtocolCombo.ItemIndex := 0;
      'UDP':
        ProtocolCombo.ItemIndex := 0;
      'TCP':
        ProtocolCombo.ItemIndex := 1;
    else
      MsgBox('Unknown protocol.', mbError, MB_OK);
    end;

    case Security.Encoding of
      '':
        EncodingCombo.ItemIndex := 0;
      'UTF-8':
        EncodingCombo.ItemIndex := 0;
      'Base64':
        EncodingCombo.ItemIndex := 1;
    else
      MsgBox('Unknown encoding.', mbError, MB_OK);
    end;

    SecretMemo.Text := Security.Secret;
    SecretAuthMemo.Text := Security.SecretAuth;

    case Security.SecretAuthType of
      '':
        SecretAuthTypeCombo.ItemIndex := 0;
      'Default':
        SecretAuthTypeCombo.ItemIndex := 0;
      'MD5':
        SecretAuthTypeCombo.ItemIndex := 1;
      'SHA1':
        SecretAuthTypeCombo.ItemIndex := 2;
      'SHA256':
        SecretAuthTypeCombo.ItemIndex := 3;
      'SHA384':
        SecretAuthTypeCombo.ItemIndex := 4;
      'SHA512':
        SecretAuthTypeCombo.ItemIndex := 5;
      'SHA3_256':
        SecretAuthTypeCombo.ItemIndex := 6;
      'SHA3_512':
        SecretAuthTypeCombo.ItemIndex := 7;
    end;
  end;
end;

function EditSecurity(Parent: TForm; var Security: TSecurityConfig) : Boolean;
var
  Dialog: TSetupForm;

begin
  Dialog := CreateCustomForm(ScaleX(SecurityDialogWidth), ScaleY(200), False, True);
  Dialog.Caption := 'Single Packet Authorization';
  Dialog.BorderStyle := bsDialog;

  SecurityControls := CreateSecurityControls(Dialog);

  UpdateSecurityControls(SecurityControls, Security);

  OnSecurityMethodChange(nil);
  
  try
    Dialog.FlipAndCenterIfNeeded(True, Parent, False);
    
    if Dialog.ShowModal() = mrOk then with SecurityControls do
    begin
      Security.Method := MethodCombo.Text;

      case MethodCombo.ItemIndex of
        0:
          Security.Method := '';
        1:
          Security.Method := 'plain';
        2:
          Security.Method := 'fko';
      end;

      Security.Port := PortEdit.Text;

      case ProtocolCombo.ItemIndex of
        0:
          Security.Protocol := 'UDP';
        1:
          Security.Protocol := 'TCP';
      end;

      case EncodingCombo.ItemIndex of
        0:
          Security.Encoding := 'UTF-8';
        1:
          Security.Encoding := 'Base64';
      end;

      if SecretMemo.Enabled then
        Security.Secret := SecretMemo.Text
      else
        Security.Secret := '';

      if SecretAuthMemo.Enabled then
        Security.SecretAuth := SecretAuthMemo.Text
      else
        Security.SecretAuth := '';

      if SecretAuthTypeCombo.Enabled then
        Security.SecretAuthType := SecretAuthTypeCombo.Text
      else
        Security.SecretAuthType := '';

      Result := True;
    end
    else
      Result := False;
  finally
    Dialog.Free();
  end;
end;