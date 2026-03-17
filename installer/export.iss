[Code]

var
  ExportIniPath: String;

procedure ExportServices(Section: String; Host: THostConfig);
var
  I: Integer;
  Service: TServiceConfig;

begin
  for I := 0 to GetArrayLength(Host.Services) - 1 do
  begin
    Service := Host.Services[I];
    
    SetIniString(Section, Service.Name, Service.Port + '/' + LowerCase(Host.Services[I].Protocol), ExportIniPath);
  end;
end;

procedure ExportSecurity(Host: THostConfig);
var
  Section: String;
  Security: TSecurityConfig;
begin
  Section := Host.Name + ':Security';
  Security := Host.Security;

  SetIniString(Section, 'method', Security.Method, ExportIniPath);
  SetIniString(Section, 'protocol', Security.Protocol, ExportIniPath);
  SetIniString(Section, 'port', Security.Port, ExportIniPath);
  SetIniString(Section, 'encoding', Security.Encoding, ExportIniPath);
  SetIniString(Section, 'secret', Security.Secret, ExportIniPath);
  SetIniString(Section, 'auth', Security.SecretAuth, ExportIniPath);
  SetIniString(Section, 'digest', Security.SecretAuthType, ExportIniPath);
end;

procedure ExportAutomation(Host: THostConfig);
var
  Section: String;
  Automation: THostAutomationConfig;
begin
  Section := Host.Name + ':Automation';
  Automation := Host.Automation;

  if Automation.MagicPacket <> '' then
    SetIniString(Section, 'magic', Automation.MagicPacket, ExportIniPath);
  if Automation.ServiceDemand <> '' then
    SetIniString(Section, 'service', Automation.ServiceDemand, ExportIniPath);
  if Automation.Demand <> '' then
    SetIniString(Section, 'demand', Automation.Demand, ExportIniPath);
  if Automation.Idle <> '' then
    SetIniString(Section, 'idle', Automation.Idle, ExportIniPath);
end;

procedure ExportHosts(Section: String; Config: THostServiceConfig);
var
  I: Integer;
  Host: THostConfig;
  Address: String;
begin
  for I := 0 to GetArrayLength(Config.Hosts) - 1 do
  begin
    Host := Config.Hosts[I];
    
    Address := '';

    if Host.MacAddressAuto then
      Address := Address + 'auto'
    else
      Address := Address + Host.MacAddress;

    Address := Address + '|';

    if Host.IPAddressAuto then
      Address := Address + 'auto'
    else
      Address := Address + FormatIP(Host.IPAddress, False);
    
    SetIniString(Section, Host.Name, Address, ExportIniPath);

    ExportServices(Host.Name, Host);

    ExportSecurity(Host);
    ExportAutomation(Host);
  end;
end;

procedure ExportHostServiceConfig(Path: String);
var
  I: Integer;

begin
  ExportIniPath := Path;

  ExportServices('Services', LocalHostConfig.Hosts[0]);
  
  ExportHosts('RemoteHosts', RemoteHostsConfig);

  if ShouldConfigureVirtualMachines then
    for I := 0 to GetArrayLength(VirtualHostsConfig) - 1 do
      ExportHosts('VirtualHosts', VirtualHostsConfig[0]);
end;