[code]

function Max(A, B: Integer): Integer;
begin
  if A > B then
    Result := A
  else
    Result := B;
end;

function Min(A, B: Integer): Integer;
begin
  if A < B then
    Result := A
  else
    Result := B;
end;

function VersionGreaterOrEqual(const Required, Current: string): Boolean;
var
  R, C: TArrayOfString;
  I, Ri, Ci: Integer;
begin
  Result := True;

  R := StringSplit(Required, ['.'], stAll);
  C := StringSplit(Current, ['.'], stAll);
  
  for I := 0 to Max(High(R), High(C)) do
  begin
    if I <= High(R) then
      Ri := StrToIntDef(R[I], 0)
    else
      Ri := 0;

    if I <= High(C) then
      Ci := StrToIntDef(C[I], 0)
    else
      Ci := 0;

    if Ci > Ri then
      Result := True
    else if Ci < Ri then
      Result := False;
  end;
end;

procedure VersionLabelOnLinkClick(Sender: TObject; const Link: string; LinkType: TSysLinkType);
var
  ErrorCode: Integer;
begin
  ShellExecAsOriginalUser('open', Link, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

procedure CreateVersionLabel();
var
  DummyLabel: TLabel;
  VersionLabel: TNewLinkLabel;
begin
  DummyLabel := TLabel.Create(WizardForm);
  DummyLabel.Caption := 'Version';
    
  VersionLabel := TNewLinkLabel.Create(WizardForm);
  
  if not ('{#MyAppVersion}' = '') then
    VersionLabel.Caption := 'Version <a href="https://github.com/MadWizardDE/Desomnia/releases/tag/v{#MyAppVersion}">{#MyAppVersion}</a>';

  VersionLabel.Left := ScaleX(10);
  VersionLabel.Top := WizardForm.CancelButton.Top + (WizardForm.CancelButton.Height / 2) - (DummyLabel.Height / 2)
  VersionLabel.Anchors := [akLeft, akBottom];
  VersionLabel.OnLinkClick := @VersionLabelOnLinkClick;
  VersionLabel.UseVisualStyle := False;
  VersionLabel.Parent := WizardForm;
end;
