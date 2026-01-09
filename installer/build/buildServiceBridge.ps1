Param([string]$TargetDirectory)

$sourceParentDirectory = "..\..\plugins\DesomniaServiceSessionBridge"

$publishBridge =
@(
    '-c', 'Release',
    '-f', "net$env:VERSION_NET-windows8.0",

    '--no-self-contained',

    '-p:DebugType=None', 
    '-p:DebugSymbols=false',
    '-p:PublishSingleFile=false'
)

Publish-Project -Source "$sourceParentDirectory\DesomniaServiceBridge" -Target $TargetDirectory -Parameters $publishBridge

$publishMinion = 
@(
    '/p:Configuration=Release'
)

Publish-FrameworkProject -Source "$sourceParentDirectory\DesomniaSessionMinion" -Target "$TargetDirectory\minion" -Parameters $publishMinion