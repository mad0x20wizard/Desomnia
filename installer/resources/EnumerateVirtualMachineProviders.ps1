$hyperv = Get-WindowsOptionalFeature -FeatureName Microsoft-Hyper-V-All -Online

if ($hyperv.State -eq "Enabled")
{
    "HyperV"
}
