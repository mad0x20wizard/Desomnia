[Code]

const 
  hsdsNone          = 0;
  hsdsHost          = 1; // 2^0
  hsdsHostEdit      = 2; // 2^1
  hsdsHostSelect    = 4; // 2^2
  hsdsMacEdit       = 8; // 2^3
  hsdsIPEdit        = 16; // 2^4
  hsdsSecurity      = 32; // 2^5

type THostServiceDialogStyle = Integer;

type
  TConfigureServicesDialogControls = record
    Style: THostServiceDialogStyle;

    HostLabel: TNewStaticText;
    HostCombo: TNewComboBox;
    HostAddButton: TNewButton;
    HostRemoveButton: TNewButton;
    
    HostComboTyping: Boolean;
    
    BevelTop: TBevel;

    MacLabel: TNewStaticText;
    MacEdit: TNewEdit;
    MacAutoCheckbox: TNewCheckBox;
    MacHelpLabel: TNewStaticText;

    IPLabel: TNewStaticText;
    IPInput: HWND;
    IPAutoCheckbox: TNewCheckBox;

    ServicesLabel: TNewStaticText;
    ServicesList: TNewCheckListBox;
    ServiceAddButton: TNewButton;
    ServiceRemoveButton: TNewButton;
    ServicesHelpLabel: TNewStaticText;

    SecurityButton: TNewButton;
    SecurityIcon: TBitmapImage;

    OkButton: TNewButton;
    CancelButton: TNewButton;

    Rebuilding: Boolean;
  end;


var
  ConfigureServicesDialog: TSetupForm;
  ConfigureServicesControls: TConfigureServicesDialogControls;
  ConfigureServicesConfig: THostServiceConfig;

procedure UpdateHostServicesForm(Config: THostServiceConfig); forward;

#include "HostServiceDialog/validation.iss"
#include "HostServiceDialog/events.iss"
#include "HostServiceDialog/controls.iss"
#include "HostServiceDialog/configure.iss"

function ConfigureHostServices(Title: String; Config: THostServiceConfig; Style: THostServiceDialogStyle) : THostServiceConfig;
var
  ConfigCopy: THostServiceConfig;
begin
  ConfigureServicesDialog := CreateConfigureServicesDialog(Style);
  ConfigureServicesDialog.FlipAndCenterIfNeeded(True, WizardForm, False);
  ConfigureServicesDialog.Caption := Title;

  ConfigCopy := CopyHostServiceConfig(Config); ConfigureServicesConfig := ConfigCopy;
  
  SanitizeHostIndex(ConfigureServicesConfig);

  UpdateHostServicesForm(ConfigureServicesConfig);
  
  try
    if ConfigureServicesDialog.ShowModal = mrOk then
      Result := ConfigureServicesConfig
    else
      Result := Config;
  finally
    ConfigureServicesDialog.Free();
  end;
end;