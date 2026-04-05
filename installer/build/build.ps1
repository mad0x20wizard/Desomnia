param([string]$Version = "")

$components = "./components"

# Build application components
. "./buildApplication.ps1" -ComponentsDirectory $components

# Build Installer
. "./buildSetup.ps1" -Version $Version

Remove-Item -Path $components -Recurse -Force

exit 0

#Start-Sleep -Seconds 1;