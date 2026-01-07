#include "CodeDependencies.iss"

[Code]

const
  NPCAP_VERSION = '1.85';
  
var
  HyperVInstalled: Boolean;
  
procedure CheckVirtualSupport;
var
  ScriptFile: string;
  ResultCode: Integer;
  I: Integer;
  
  ExecOutput: TExecOutput;
  Lines: TArrayOfString;
begin
  HyperVInstalled := false;

  ExtractTemporaryFile('CheckVirtualSupport.ps1');
  
  ScriptFile := ExpandConstant('{tmp}\CheckVirtualSupport.ps1')

  // Run PowerShell script and capture output
  if ExecAndCaptureOutput('powershell.exe', '-ExecutionPolicy Bypass -File "' + ScriptFile + '"', ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, ResultCode, ExecOutput) then
  begin
    Lines := ExecOutput.StdOut;
    
    for I := 0 to GetArrayLength(Lines) - 1 do
    begin
      if Lines[I] = 'HyperV' then
      begin
        HyperVInstalled := true;
      end;
    end;
  end;
end;

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

function IsHyperVInstalled(): Boolean;
begin
  Result := HyperVInstalled;   
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
