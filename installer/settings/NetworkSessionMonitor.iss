[Code]

function ShouldConfigureNetworkSessionMonitor(): Boolean;
begin
  Result := ShouldConfigureDesomnia;
end;

function NetworkSessionMonitorPrefs(Param: String) : String;
begin
  Result := '???';

  if Param = 'Track' then
    if not HasExistingConfig() then
      Result := 'everything'
    else
      Result := 'custom';
end;
