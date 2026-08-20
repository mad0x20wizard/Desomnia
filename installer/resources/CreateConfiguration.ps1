param( [Parameter(Mandatory=$true)][string]$IniPath, [Parameter(Mandatory=$true)][string]$XmlPath )

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path

Add-Type -Path "$scriptDirectory\IniFile.cs"

# Load INI file
$ini = [IniFile]::new($IniPath)

# Helper to add XML elements
function Add-XmlElement
{
    param(
        [Parameter(Mandatory)]
        [System.Xml.XmlNode] $Parent,

        [Parameter(Mandatory)]
        [string] $Name,

        [string] $Text,

        [System.Collections.IDictionary] $Attributes
    )

    $doc = if ($Parent -is [System.Xml.XmlDocument]) { $Parent } else { $Parent.OwnerDocument }

    $element = $doc.CreateElement($Name)

    if ($Text) {
        $element.InnerText = $Text
    }

    Add-XmlAttributes $element $Attributes

    [void]$Parent.AppendChild($element)

    return $element
}

function Add-XmlAttributes
{
    param(
        [Parameter(Mandatory)]
        [System.Xml.XmlNode] $Node,

        [System.Collections.IDictionary] $Attributes
    )

    $doc = if ($Node -is [System.Xml.XmlDocument]) { $Node } else { $Node.OwnerDocument }

    if ($Attributes) {
        foreach ($key in $Attributes.Keys) {
            if ($value = $Attributes[$key])
            {
                $attr = $doc.CreateAttribute($key)
                $attr.Value = $value

                [void]$Node.Attributes.Append($attr)
            }
        }
    }
}

function Add-Services
{
    param(
        [Parameter(Mandatory)]
        [System.Xml.XmlNode] $Parent,

        [IniFile+Section] $Services
    )

    foreach ($name in $Services)
    {
        $parts = $Services[$name] -split "/"

        $port = $parts[0]
        $protocol = $parts[1]

        if ($protocol -eq "tcp")
        {
            $protocol = $null
        }
        else
        {
            $protocol = $protocol.ToUpper()
        }

        Add-XmlElement $Parent "Service" $null @{
            name =      $name
            protocol =  $protocol
            port =      $port
        }
    }
}

function Add-Security
{
    param(
        [Parameter(Mandatory)]
        [System.Xml.XmlNode] $element,

        [IniFile+Section] $Security
    )

    if ($Security -and $Security['method'])
    {
        Add-XmlAttributes $element @{
            knockMethod =           $Security['method']
            knockProtocol =         $Security['protocol']
            knockPort =             $Security['port']
            knockSecretEncoding =   $Security['encoding']
            knockSecret =           $Security['secret']
            knockSecretAuth =       $Security['auth']
            knockSecretAuthType =   $Security['digest']
        }
    }
}

function Add-Automation
{
    param(
        [Parameter(Mandatory)]
        [System.Xml.XmlNode] $element,

        [IniFile+Section] $Automation
    )

    if ($Automation)
    {
        if ($element.PSBase.Name -eq "VirtualHost")
        {
            [void]$element.Attributes.Append($element.OwnerDocument.CreateAttribute('onMagicPacket'))
        }

        Add-XmlAttributes $element @{
            onServiceDemand =   $Automation['service']
            onDemand =          $Automation['demand']
            onIdle =            $Automation['idle'] -replace '\s', ''
            onMagicPacket =     $Automation['magic']
        }
    }
}

function Add-Hosts
{
    param(
        [Parameter(Mandatory)]
        [System.Xml.XmlNode] $element,

        [IniFile+Section] $Hosts,

        [Parameter(Mandatory)]
        [string] $Type,

        [string] $AutoDetect

    )

    foreach ($name in $Hosts)
    {
        $addr = $Hosts[$name] -split "\|";
        $mac = $addr[0]
        $ip = $addr[1]

        $auto = @()
        if ($mac -eq "auto") {
            $mac = $null
            $auto += "MAC"
        }

        if ($ip -eq "auto") {
            $ip = $null
            $auto += "IPv4|IPv6"
        }

        if ($addr[2] -eq "auto") {
            $auto += "Service"
        }

        if ($AutoDetect) {
            $auto += $AutoDetect
        }

        $remote = Add-XmlElement $monitor $Type $null @{
            name        = $name
            autoDetect  = $auto -join "|"
            MAC         = $mac
            IPv4        = $ip
        }

        Add-Automation $remote $ini[$name + ':Automation']

        Add-Security $remote $ini[$name + ':Security']

        Add-Services $remote $ini[$name]
    }
}

# Create XML document
$xml = New-Object System.Xml.XmlDocument

[void]$xml.AppendChild($xml.CreateXmlDeclaration("1.0", "UTF-8", $null))

# The format version is declared on the root element (see docs/concepts/version.rst);
# the <?config?> header is only written when the user asks for a non-default migration
# policy ("transient" is the default and needs no declaration)
$config      = $ini["config"]
$version     = if ($config -and $config["version"])     { $config["version"] }     else { "2" }
$autoMigrate = if ($config -and $config["autoMigrate"]) { $config["autoMigrate"] } else { $null }

if ($autoMigrate)
{
    [void]$xml.AppendChild($xml.CreateProcessingInstruction("config", "autoMigrate=""$autoMigrate"""))
}

# Root element
$root = Add-XmlElement $xml "SystemMonitor" $null ([ordered]@{
    version  = $version
    timeout  = $ini["SystemMonitor"]["timeout"] -replace '\s', ''
    onIdle   = $ini["SystemMonitor"]["idle"]    -replace '\s', ''
    onDemand = $ini["SystemMonitor"]["demand"]  -replace '\s', ''
})


# Static monitors
Add-XmlElement $root "SessionMonitor"
Add-XmlElement $root "NetworkSessionMonitor"
Add-XmlElement $root "PowerRequestMonitor"

if ($network = $ini["NetworkMonitor"])
{
    $name = $network["name"]
    if ($name -eq "auto")  {
        $name = $null
    }

    $autoDetect = "IPv4|IPv6|Router"
    if ($network["autoDetect"]) {
        $autoDetect = $autoDetect + "|" + $network["autoDetect"]
    }

    $monitor = Add-XmlElement $root "NetworkMonitor" $null @{
        name       = $name
        autoDetect = $autoDetect
        interface  = $network["interface"]
        network    = $network["network"]
        watchMode  = $network["mode"]
        handoff    = $network["handoff"]
        handoffDuration = $network["handoffDuration"] -replace '\s', ''
        allowWakeOnLAN  = $network["allowWakeOnLAN"]
    }

    Add-Services $monitor $ini["Services"]
    Add-Hosts $monitor $ini["RemoteHosts"] "RemoteHost" -AutoDetect $network["hostAutoDetect"]
    Add-Hosts $monitor $ini["VirtualHosts"] "VirtualHost"

}

if ($duo = $ini["DuoSessionMonitor"])
{
    Add-XmlElement $root "DuoSessionMonitor" $null @{
        onInstanceDemand = $duo["demand"]   -replace '\s', ''
        onInstanceIdle   = $duo["idle"]     -replace '\s', ''
    }
}

$xmlDirectory = Split-Path -Parent $XmlPath
if (-not (Test-Path $xmlDirectory)) {
    New-Item -ItemType Directory -Path $xmlDirectory -Force | Out-Null
}

# Save XML
$xml.Save($XmlPath)

$ini["config:monitor.xml"]["SHA256"] = (Get-FileHash $XmlPath -Algorithm SHA256).Hash

Write-Host "XML configuration created at $XmlPath"

# Read-Host "Please press any key to continue"
