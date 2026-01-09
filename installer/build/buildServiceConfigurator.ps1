Param([string]$TargetDirectory)

$parameters = @(
    '-c', 'Release',
    '-f', "net$env:VERSION_NET-windows8.0",
    '-r', 'win-x64',

    '--no-self-contained',

    '-p:DebugType=None', 
    '-p:DebugSymbols=false',
    '-p:PublishSingleFile=true', 
    '-p:PublishReadyToRun=true'
)

Publish-Project -Source "..\DesomniaServiceConfigurator" -Target $TargetDirectory -Parameters $parameters
