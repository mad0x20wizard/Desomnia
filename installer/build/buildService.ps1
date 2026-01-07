Param([string]$TargetDirectory, [string]$Arch)

$parameters = @(
    '-f', "net$env:VERSION_NET-windows8.0",
    '-c', 'Release',
    '-r', "win-$Arch",

    '--no-self-contained',

    '-p:DebugType=None', 
    '-p:DebugSymbols=false',
    '-p:PublishSingleFile=false'
)

Publish-Project -Source "..\..\DesomniaService" -Target $TargetDirectory -Parameters $parameters
