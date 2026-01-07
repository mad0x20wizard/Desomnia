Param([string]$TargetDirectory, [string]$Arch)

$parameters = @(
    '-f', "net$env:VERSION_NET-windows8.0",
    '-c', 'Release',
    '-r', "win-$Arch",

    '--no-self-contained',

    '-p:DebugType=None', 
    '-p:DebugSymbols=false',
    '-p:PublishSingleFile=true', 
    '-p:PublishReadyToRun=true'
)

Publish-Project -Source "..\..\helper\DesomniaServiceHelper" -Target $TargetDirectory -Parameters $parameters
