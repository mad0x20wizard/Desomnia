# Path to Inno Setup script
$setupScript = "..\setup.iss"

# Inno Setup compiler path (assumes it's in PATH, otherwise set full path)
$compiler = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

# $arguments = "/DDisableBridge"
$arguments = "/DMyAppVersion=$env:VERSION_DESOMNIA"

# Run Inno Setup
Write-Host "Building installer from '$setupScript'..."

$result = & $compiler $arguments $setupScript 2>&1

if ($LASTEXITCODE -eq 0)
{
    #Write-Host "✅ Inno Setup compilation succeeded."
}
else
{
    $result | Write-Output

    Exit-WithError -Message "❌ Inno Setup compilation failed!"
}
