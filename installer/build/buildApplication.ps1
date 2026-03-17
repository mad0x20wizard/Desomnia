Param([string]$ComponentsDirectory)

$env:VERSION_NET = "9.0"

. "./functions.ps1"

# Build Service (x64)
. "./buildService.ps1" -TargetDirectory "$ComponentsDirectory/service/x64" -Arch 'x64'
. "./buildServiceHelper.ps1" -TargetDirectory "$ComponentsDirectory/service/x64" -Arch 'x64'
# Build Service (arm64)
. "./buildService.ps1" -TargetDirectory "$ComponentsDirectory/service/arm64" -Arch 'arm64'
. "./buildServiceHelper.ps1" -TargetDirectory "$ComponentsDirectory/service/arm64" -Arch 'arm64'

# Build Plugins
. "./buildPlugins.ps1" -TargetParentDirectory "$ComponentsDirectory/plugins"
. "./buildServiceBridge.ps1" -TargetDirectory "$ComponentsDirectory/plugins/DesomniaServiceBridge"
