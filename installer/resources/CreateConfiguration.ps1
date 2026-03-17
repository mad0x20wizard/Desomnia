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

        [hashtable] $Attributes
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

        [hashtable] $Attributes
    )

    $doc = if ($Node -is [System.Xml.XmlDocument]) { $Node } else { $Node.OwnerDocument }

    if ($Attributes) {
        foreach ($key in $Attributes.Keys) {
            if ($value = $Attributes[$key])
            {
                $attr = $doc.CreateAttribute($key)
                $attr.Value = $value

                [void]$element.Attributes.Append($attr)
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
            knockEncoding =         $Security['encoding']
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
        [string] $Type

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

# Root element
$root = Add-XmlElement $xml "SystemMonitor" $null @{
    version  = $ini["SystemMonitor"]["version"]
    timeout  = $ini["SystemMonitor"]["timeout"] -replace '\s', ''
    onIdle   = $ini["SystemMonitor"]["idle"]    -replace '\s', ''
    onDemand = $ini["SystemMonitor"]["demand"]  -replace '\s', ''
}


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

    $monitor = Add-XmlElement $root "NetworkMonitor" $null @{
        name       = $name
        autoDetect = "IPv4|IPv6|Router"
        interface  = $network["interface"]
        network    = $network["network"]
        watchMode  = $network["mode"]
    }

    Add-Services $monitor $ini["Services"]
    Add-Hosts $monitor $ini["RemoteHosts"] "RemoteHost"
    Add-Hosts $monitor $ini["VirtualHosts"] "VirtualHost"

}

if ($duo = $ini["DuoStreamMonitor"])
{
    Add-XmlElement $root "DuoStreamMonitor" $null @{
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

Write-Host "XML configuration created at $XmlPath"

# Read-Host "Please press any key to continue"
