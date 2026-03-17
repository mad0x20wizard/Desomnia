[Code]

type
  TServiceConfig = record
    Name: String;
    Port: String;
    Protocol: String;
  end;

type
  TSecurityConfig = record
    Method: String;
    Protocol: String;
    Port: String;

    Encoding: String;

    Secret: String;
    SecretAuth: String;
    SecretAuthType: String;
  end;

type
  THostAutomationConfig = record
    MagicPacket: String;
    ServiceDemand: String;
    Demand: String;
    Idle: String;
  end;

type
  THostConfig = record
    Name: String;

    MacAddress: String;
    MacAddressAuto: Boolean;

    IPAddress: IPAddress;
    IPAddressAuto: Boolean;

    Services: array of TServiceConfig;
    Security: TSecurityConfig;

    Automation: THostAutomationConfig;
  end;

type THostServiceConfig = record
  Hosts: array of THostConfig;
  Index: Integer;
end;

function HasExistingConfig(): Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\config\monitor.xml'));
end;

procedure SanitizeHostIndex(var Config: THostServiceConfig);
begin
  if GetArrayLength(Config.Hosts) = 0 then
    Config.Index := -1
  else if Config.Index > GetArrayLength(Config.Hosts) - 1 then
    Config.Index := GetArrayLength(Config.Hosts) - 1
  else if Config.Index < 0 then
    Config.Index := 0;
end;

// Copy Function //

function CopySecurityConfig(Security: TSecurityConfig): TSecurityConfig;
begin
  Result.Method := Security.Method;
  Result.Protocol := Security.Protocol;
  Result.Port := Security.Port;

  Result.Encoding := Security.Encoding;

  Result.Secret := Security.Secret;
  Result.SecretAuth := Security.SecretAuth;
  Result.SecretAuthType := Security.SecretAuthType;
end;


function CopyHostConfig(Host: THostConfig): THostConfig;
var
  I: Integer;
begin
  Result.Name := Host.Name;
  Result.MacAddress := Host.MacAddress;
  Result.MacAddressAuto := Host.MacAddressAuto;
  
  Result.IPAddress := Host.IPAddress;
  Result.IPAddressAuto := Host.IPAddressAuto;

  SetArrayLength(Result.Services, GetArrayLength(Host.Services));
  
  for I := 0 to GetArrayLength(Host.Services) - 1 do
  begin
    Result.Services[I].Name := Host.Services[I].Name;
    Result.Services[I].Port := Host.Services[I].Port;
    Result.Services[I].Protocol := Host.Services[I].Protocol;
  end;

  Result.Security := CopySecurityConfig(Host.Security);

  Result.Automation := Host.Automation;
end;

function CopyHostServiceConfig(Config: THostServiceConfig): THostServiceConfig;
var
  I: Integer;
begin
  Result.Index := Config.Index;
  
  SetArrayLength(Result.Hosts, GetArrayLength(Config.Hosts));
  
  for I := 0 to GetArrayLength(Config.Hosts) - 1 do
    Result.Hosts[I] := CopyHostConfig(Config.Hosts[I]);
end;

