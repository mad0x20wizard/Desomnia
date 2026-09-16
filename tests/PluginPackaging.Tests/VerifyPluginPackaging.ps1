param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$outputRoot = Join-Path $repositoryRoot ('artifacts\plugin-packaging\' + [guid]::NewGuid().ToString('N'))

function Get-ProjectInfo([string]$ProjectPath) {
    $result = & dotnet msbuild $ProjectPath -nologo -getProperty:AssemblyName,IsDesomniaPlugin -getItem:DesomniaHostProject,DesomniaHostPackage
    if ($LASTEXITCODE -ne 0) { throw "Cannot evaluate $ProjectPath" }
    return ($result -join [Environment]::NewLine) | ConvertFrom-Json
}

# Read opt-ins from source so the check also covers newly added plugins.
$projects = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'plugins') -Filter '*.csproj' -Recurse -File)
$plugins = @($projects | Where-Object {
    [xml]$projectXml = Get-Content -Raw -LiteralPath $_.FullName
    $projectXml.SelectNodes('/Project/PropertyGroup/IsDesomniaPlugin') | Where-Object InnerText -EQ 'true'
})
if ($plugins.Count -eq 0) { throw 'No Desomnia plugins found.' }

$firstPluginInfo = Get-ProjectInfo $plugins[0].FullName
$hostAssemblyNames = @($firstPluginInfo.Items.DesomniaHostProject | ForEach-Object {
    (Get-ProjectInfo $_.FullPath).Properties.AssemblyName
})
$hostPackageNames = @($firstPluginInfo.Items.DesomniaHostPackage.Identity)
$hostFiles = @(
    'System.ServiceProcess.ServiceController.dll',
    'System.Diagnostics.EventLog.dll',
    'System.Diagnostics.EventLog.Messages.dll',
    'KernelTraceControl.dll', 'KernelTraceControl.Win61.dll', 'msdia140.dll',
    'Dia2Lib.dll', 'TraceReloggerLib.dll',
    'microsoft.management.infrastructure.native.unmanaged.dll'
)
$requiredPrivateDependencies = @{
    DuoStreamIntegration = @('Refit', 'WindowsFirewallHelper')
    DesomniaServiceBridge = @('DesomniaPipe', 'MessagePack')
}

foreach ($plugin in $plugins) {
    $info = Get-ProjectInfo $plugin.FullName
    if ($info.Properties.IsDesomniaPlugin -ne 'true' -or $info.Items.DesomniaHostProject.Count -eq 0) {
        throw "Shared plugin rules are missing for $($plugin.Name)"
    }
    $assemblyName = $info.Properties.AssemblyName

    foreach ($runtime in @('portable', 'win-x64')) {
        # A fresh directory distinguishes files copied by this publish from old deployment files.
        $publishDirectory = Join-Path $outputRoot "$assemblyName\$runtime"
        $arguments = @(
            'publish', $plugin.FullName, '-c', $Configuration,
            '--no-self-contained', '--ignore-failed-sources', '-p:NuGetAudit=false',
            '-p:PublishSingleFile=false', '-o', $publishDirectory, '-v:quiet'
        )
        if ($runtime -ne 'portable') { $arguments += @('-r', $runtime) }
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $assemblyName ($runtime)" }

        $files = @(Get-ChildItem -LiteralPath $publishDirectory -Recurse -File)
        $unexpected = @($files | Where-Object { $_.BaseName -in $hostAssemblyNames -or $_.Name -in $hostFiles })
        if ($unexpected.Count -ne 0) {
            throw "Host files copied by $assemblyName ($runtime): $($unexpected.FullName -join ', ')"
        }

        $depsPath = Join-Path $publishDirectory "$assemblyName.deps.json"
        $deps = Get-Content -Raw -LiteralPath $depsPath | ConvertFrom-Json
        $libraryNames = @($deps.libraries.PSObject.Properties.Name | ForEach-Object { $_.Split('/')[0] })
        $unexpectedLibraries = @($libraryNames | Where-Object { $_ -in $hostAssemblyNames -or $_ -in $hostPackageNames })
        if ($unexpectedLibraries.Count -ne 0) {
            throw "Host dependencies remain in ${depsPath}: $($unexpectedLibraries -join ', ')"
        }

        foreach ($required in @($assemblyName) + @($requiredPrivateDependencies[$assemblyName])) {
            if (!$required) { continue }
            if ("$required.dll" -notin $files.Name -or $required -notin $libraryNames) {
                throw "Required dependency $required is missing from $assemblyName ($runtime)"
            }
        }
        Write-Host "PASS: $assemblyName ($runtime)"
    }
}

# A directory-wide import must not turn helper applications or private libraries into plugins.
foreach ($project in $projects | Where-Object { $_.FullName -notin $plugins.FullName }) {
    $info = Get-ProjectInfo $project.FullName
    if ($info.Properties.IsDesomniaPlugin -eq 'true' -or @($info.Items.DesomniaHostProject).Count -ne 0) {
        throw "Plugin rules unexpectedly applied to $($project.Name)"
    }
}

Write-Host "Verified $($plugins.Count) plugins in both publish configurations. Output: $outputRoot"
