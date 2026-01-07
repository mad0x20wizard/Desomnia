$env:VERSION_NET = "9.0"
$env:VERSION_DESOMNIA = "3.0.0-alpha2"

. "./functions.ps1"

# Build Service (x64)
. "./buildService.ps1" -TargetDirectory "./components/service/x64" -Arch 'x64'
. "./buildServiceHelper.ps1" -TargetDirectory "./components/service/x64" -Arch 'x64'
# Build Service (arm64)
. "./buildService.ps1" -TargetDirectory "./components/service/arm64" -Arch 'arm64'
. "./buildServiceHelper.ps1" -TargetDirectory "./components/service/arm64" -Arch 'arm64'

# Build Plugins
. "./buildPlugins.ps1" -TargetParentDirectory "./components/plugins"
. "./buildServiceBridge.ps1" -TargetDirectory "./components/plugins/DesomniaServiceBridge"

# Build Installer
. "./buildServiceConfigurator.ps1" -TargetDirectory "./components"
. "./buildSetup.ps1"

# Remove-Item -Path "./components" -Recurse -Force

exit 0

#Start-Sleep -Seconds 1;