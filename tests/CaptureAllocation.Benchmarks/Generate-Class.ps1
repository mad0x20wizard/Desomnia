param([Parameter(Mandatory = $true)][string]$Destination)
$ErrorActionPreference = 'Stop'
$sourcePath = Join-Path $PSScriptRoot '../../modules/NetworkMonitor/Monitor/Capture/NetworkCapture.cs'
$source = [IO.File]::ReadAllText($sourcePath)
# Fail if the production shape changes instead of silently comparing different implementations.
foreach ($expected in @('public struct NetworkCapture', 'internal readonly EthernetPacket Packet', 'public NetworkCapture(EthernetPacket packet) : this()')) {
    if (-not $source.Contains($expected)) { throw "Update class generation: missing '$expected'" }
}
$class = $source.Replace('NetworkCapture', 'NetworkCaptureClass').
    Replace('public struct NetworkCaptureClass', 'public sealed class NetworkCaptureClass').
    Replace('internal readonly EthernetPacket Packet', 'internal EthernetPacket Packet').
    Replace('public NetworkCaptureClass(EthernetPacket packet) : this()', 'public NetworkCaptureClass(EthernetPacket packet)')
$hash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
$class = "// Generated from production NetworkCapture.cs; SHA256: $hash`n#nullable enable`n" + $class
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Destination))) | Out-Null
[IO.File]::WriteAllText($Destination, $class)
