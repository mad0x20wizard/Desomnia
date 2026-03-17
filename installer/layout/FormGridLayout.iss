[Code]

const
  DefaultPadding = 10;
  DefaultColumnGap = 5;
  DefaultRowGap = 10;

type
  TFormGridLayout = record
    Form: TSetupForm;
    PaddingTop: Integer;
    PaddingLeft: Integer;
    PaddingRight: Integer;
    PaddingBottom: Integer;
    LabelWidth: Integer;
    ButtonWidth: Integer;
    ButtonHeight: Integer;
    InputWidth: Integer;
    InputLeft: Integer;
    InputRight: Integer;
    ColumnGap: Integer;
    RowGap: Integer;
    Top: Integer;
  end;

function CreateFormGridLayout(IForm: TSetupForm; ILabelWidth: Integer): TFormGridLayout;
begin

  with Result do
  begin
    Form := IForm

    PaddingTop := ScaleX(DefaultPadding);
    PaddingLeft := ScaleX(DefaultPadding);
    PaddingRight := ScaleX(DefaultPadding);
    PaddingBottom := ScaleX(DefaultPadding);

    LabelWidth := ILabelWidth;
    ButtonHeight := ComboBoxHeight;
    
    InputLeft := PaddingLeft + LabelWidth;
    InputRight := Form.ClientWidth - PaddingRight;
    InputWidth := InputRight - InputLeft;

    ColumnGap := ScaleX(DefaultColumnGap);
    RowGap := ScaleY(DefaultRowGap);

    Top := PaddingTop;
  end;

end;

function AddControlLabel(var Layout: TFormGridLayout; Control: TWinControl; Text: String) : TNewStaticText;
var
  StaticText: TNewStaticText;

begin
  with Layout do
  begin
    StaticText := TNewStaticText.Create(Form);
    StaticText.Parent := Form;
    StaticText.AutoSize := True;
    StaticText.Caption := Text;
    StaticText.Left := InputLeft - StaticText.Width - ColumnGap;

    if Control is TNewCheckListBox then
      StaticText.Top := Control.Top + ScaleY(2)
    else if Control is TMemo then
      StaticText.Top := Control.Top + ScaleY(2)
    else
      StaticText.Top := CenterLeftBetweenRightY(StaticText, Control);
  end;

  Result := StaticText;
end;

function AddBevelLine(var Layout: TFormGridLayout) : TBevel;
var
  Bevel: TBevel;
begin
  with Layout do
  begin
    Bevel := TBevel.Create(Form);
    Bevel.Parent := Form;
    Bevel.Top := Top;
    Bevel.Left := -1;
    Bevel.Width := Form.Width;
    Bevel.Height := 2;

    Top := Top + RowGap
  end;

  Result := Bevel;
end;

procedure LayoutFormButtons(var Layout: TFormGridLayout; Buttons: array of TNewButton);
var
  I: Integer;
  Captions: array of String;
  Right: Integer;
  Gap: Integer;

begin
  SetArrayLength(Captions, GetArrayLength(Buttons));
  for I := 0 to GetArrayLength(Buttons) - 1 do
    Captions[I] := Buttons[I].Caption;


  with Layout do
  begin
    //ButtonWidth := Form.CalculateButtonWidth(Captions); // geht nicht
    ButtonWidth := Form.CalculateButtonWidth(['Cancel',  'OK']); // TODO Krank!

    Right := InputRight;
    Gap := 0;

    for I := 0 to GetArrayLength(Buttons) - 1 do
    begin
      Buttons[I].Parent := Form;

      Buttons[I].Width := ButtonWidth
      Buttons[I].Height := ButtonHeight;

      Buttons[I].Top := Top;
      Buttons[I].Left := Right - Buttons[I].Width - Gap;

      Right := Buttons[I].Left;
      Gap := ColumnGap;
    end;
  end;
end;