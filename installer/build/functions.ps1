function Exit-WithError
{
    param ( [string] $Message )

    Write-Host $Message
    Write-Host "Press any key to continue..."
    [System.Console]::ReadKey($true) | Out-Null
    exit 1
}

function Publish-Project
{
    param ( [string] $Source, [string] $Target, [string[]] $Parameters )

    $name = Split-Path -Path $Source -Leaf
    $project = "$Source\$name.csproj"

    Write-Host "Publishing project '$name'..."

    # $Parameters = @('-p:PublishProfile=Alpha', '-o', $TargetDirectory)

    $publishResult = dotnet publish $project @Parameters -o $Target /v:minimal 2>&1        

    if ($LASTEXITCODE -eq 0)
    {
        #Write-Host "✅ Publish succeeded for $name"
    }
    else
    {
        $publishResult | Write-Output

        Exit-WithError -Message "❌ Publish failed for $name"
    }
}

function Publish-FrameworkProject
{
    param ( [string] $Source, [string] $Target, [string[]] $Parameters )

    # DOESN'T WORK
    # $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"

    # $msbuildPath = & $vswhere `
    #     -latest `
    #     -products * `
    #     -requires Microsoft.Component.MSBuild `
    #     -find MSBuild\**\Bin\MSBuild.exe

    # if (-not $msbuildPath) {
    #     throw "MSBuild.exe not found"
    # }

    # MSBuild path — you may need to adjust this based on your Visual Studio version
    $msbuild = "${env:ProgramFiles}\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe"

    $name = Split-Path -Path $Source -Leaf
    $project = "$Source\$name.csproj"

    Write-Host "Building .NET Framework 4.8 project '$name'..."

    New-Item -ItemType Directory -Path $Target -Force | Out-Null

    $msbuildResult = & "$msbuild" $project @Parameters /p:OutDir=$(Resolve-Path $Target) /v:minimal 2>&1

    if ($LASTEXITCODE -eq 0)
    {
        #Write-Host "✅ MSBuild succeeded for $frameworkProject"
    }
    else
    {
        $msbuildResult | Write-Output

        Exit-WithError -Message "❌ MSBuild failed for $name"
    }
}