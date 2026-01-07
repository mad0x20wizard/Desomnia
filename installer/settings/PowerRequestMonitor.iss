[Code]

function ShouldConfigurePowerRequestMonitor(): Boolean;
begin
  Result := ShouldConfigureDesomnia;
end;

function PowerRequestMonitorPrefs(Param: String) : String;
begin
  Result := '???';

  if Param = 'Track' then
    if not HasExistingConfig() then
      Result := 'everything'
    else
      Result := 'custom';
end;
