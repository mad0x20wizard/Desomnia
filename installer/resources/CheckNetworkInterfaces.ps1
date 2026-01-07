[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$interfaces = Get-NetAdapter -Physical | Sort-Object InterfaceIndex | ForEach-Object {
    "$($_.InterfaceGuid);$($_.Name)"
}

$interfaces -join "`n"  # Output as newline-separated lists

# $interfaces | Out-File -FilePath "network_interfaces.txt" -Encoding UTF8
