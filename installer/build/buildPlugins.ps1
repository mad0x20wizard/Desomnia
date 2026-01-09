Param([string]$TargetParentDirectory)

$plugins = @(
    @{ Name = "DuoStreamIntegration";   Framework = "net$env:VERSION_NET-windows8.0" }
    @{ Name = "HyperVSupport";          Framework = "net$env:VERSION_NET-windows8.0" }
    @{ Name = "FirewallKnockOperator";  Framework = "net$env:VERSION_NET" }
)

foreach ($plugin in $plugins)
{
    $name = $plugin.Name

    $pathSource = "..\..\plugins\$name"
    $pathTarget = "$TargetParentDirectory\$name"

    $publish = @(
        '-f', $plugin.Framework,
        '-c', 'Release',

        '--no-self-contained',

        '-p:DebugType=None', 
        '-p:DebugSymbols=false',
        '-p:PublishSingleFile=false'
    )

    Publish-Project -Source $pathSource -Target $pathTarget -Parameters $publish
}