[Code]

type
  TVirtualMachine = record
    Name: String;
    IsBridged: Boolean;
  end;

type
  TVirtualMachineProvider = record
    Name: String;
    Initialized: Boolean;

    Machines: array of TVirtualMachine;
end;

var
  VirtualProviders: array of TVirtualMachineProvider;


function EnumerateVirtualMachineProviders : array of TVirtualMachineProvider;
var
  ScriptFile: string;
  ResultCode: Integer;
  I: Integer;
  
  ExecOutput: TExecOutput;
  Lines: TArrayOfString;
begin
  ExtractTemporaryFile('EnumerateVirtualMachineProviders.ps1');
  
  ScriptFile := ExpandConstant('{tmp}\EnumerateVirtualMachineProviders.ps1');

  // Run PowerShell script and capture output
  if ExecAndCaptureOutput('powershell.exe', '-ExecutionPolicy Bypass -File "' + ScriptFile + '"', ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, ResultCode, ExecOutput) then
  begin
    Lines := ExecOutput.StdOut;

    SetArrayLength(Result, GetArrayLength(Lines));
    
    for I := 0 to GetArrayLength(Lines) - 1 do
    begin
      Result[I].Name := Lines[I];
    end;
  end;
end;

procedure EnumerateVirtualMachines(var Provider: TVirtualMachineProvider);
var
  ScriptFile: string;
  Arguments: string;
  ResultCode: Integer;
  I: Integer;
  
  ExecOutput: TExecOutput;
  Lines: TArrayOfString;

  Machine: TArrayOfString;
begin
  ExtractTemporaryFile('EnumerateVirtualMachines.ps1');
  
  ScriptFile := ExpandConstant('{tmp}\EnumerateVirtualMachines.ps1');
  Arguments := ' ' + Provider.Name;

  // Run PowerShell script and capture output
  if ExecAndCaptureOutput('powershell.exe', '-ExecutionPolicy Bypass -File "' + ScriptFile + '"' + Arguments, ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, ResultCode, ExecOutput) then
  begin
    Lines := ExecOutput.StdOut;

    SetArrayLength(Provider.Machines, GetArrayLength(Lines));
    
    for I := 0 to GetArrayLength(Lines) - 1 do
    begin
      Machine := StringSplit(Lines[I], [':'], stExcludeEmpty);

      Provider.Machines[I].Name := Machine[0];
      Provider.Machines[I].IsBridged := Machine[1] = 'external';
    end;

    Provider.Initialized := True;
  end;
end;

function HasVMProvider(Name: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 0 to GetArrayLength(VirtualProviders) - 1 do
    if VirtualProviders[I].Name = Name then
      Result := True;
end;