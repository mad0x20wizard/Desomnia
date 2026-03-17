#include "CodeDependencies.iss"

[Code]

const
  UninstallKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppId}_is1';
  UninstallStringName = 'UninstallString';

const
  NPCAP_VERSION = '1.85';
  
var
  IsReinstall: Boolean;
  
function IsNpcapInstalled(): Boolean;
var
  InstalledVersion: string;
begin
  if RegQueryStringValue(GetHKLM, 'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\NpcapInst', 'DisplayVersion', InstalledVersion) then
    Result := VersionGreaterOrEqual(NPCAP_VERSION, InstalledVersion)
  else
    Result := False;
end;

function IsDuoInstalled(): Boolean;
begin
  Result := RegKeyExists(GetHKLM, 'SOFTWARE\Duo');   
end;

function IsBridgeReady(): Boolean;
var
  Ready: String;
begin
  #ifdef DisableBridge
    Result := False;
  #else
    Result := True;
  #endif
end;

procedure Dependency_Clear;
begin
  SetLength(Dependency_Memo, 0)
  SetArrayLength(Dependency_List, 0);
end;

procedure Dependency_AddNpcap;
begin
  if not IsNpcapInstalled() then
    Dependency_Add('npcap-'+NPCAP_VERSION+'.exe', '', 'Npcap', 'https://npcap.com/dist/npcap-' + NPCAP_VERSION + '.exe', '', False, False);
end;

<event('NextButtonClick')>
function CheckDependencies(PageID: Integer): Boolean;
begin
  if PageID = wpSelectComponents then
  begin
    Dependency_Clear;
    //Dependency_AddVC2015To2022;
    if not Dependency_IsNetCoreInstalled('Microsoft.WindowsDesktop.App', 10, 0, 0) then begin
      Dependency_AddDotNet90Desktop;
    end;

    if IsComponentSelected('DesomniaService\NetworkMonitor') then
      Dependency_AddNpcap;
    if IsComponentSelected('plugins\DesomniaServiceBridge') then
      Dependency_AddDotNet48;
  end;

  Result := True;
end;

procedure CopyInstallerTo(TargetPath: String);
var
  SourceFile: String;
begin
  SourceFile := ExpandConstant('{srcexe}');
    
  ForceDirectories(ExtractFileDir(TargetPath));
  FileCopy(SourceFile, TargetPath, False);
end;


procedure AddUninstallerArguments(Arguments: String);
var
  S: string;
begin
  if RegQueryStringValue(HKEY_LOCAL_MACHINE, UninstallKey, UninstallStringName, S) then
  begin
    S := S + ' ' + Arguments;
    
    if not RegWriteStringValue(HKEY_LOCAL_MACHINE, UninstallKey, UninstallStringName, S) then
      MsgBox('Error adding arguments to uninstaller.', mbError, MB_OK);
  end else
      MsgBox('Error reading arguments of uninstaller from ' + UninstallKey, mbError, MB_OK);
end;
