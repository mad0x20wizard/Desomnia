Param([string]$ProviderName)

$hyperv = Get-WindowsOptionalFeature -FeatureName Microsoft-Hyper-V-All -Online

if (($hyperv.State -eq "Enabled") -and $ProviderName -eq "HyperV")
{
    Get-VM | ForEach-Object {
        $vm = $_
        $adapters = Get-VMNetworkAdapter -VMName $vm.Name -ErrorAction SilentlyContinue

        foreach ($adapter in $adapters) {
            if ($adapter.SwitchName) {
                $switchType = [string](Get-VMSwitch -Name $adapter.SwitchName -ErrorAction SilentlyContinue).SwitchType

                if ($switchType) {
                    ($vm.Name + ":" + $switchType.ToLower())
                } else {
                    ($vm.Name + ":unknown")
                }
            } else {
                ($vm.Name + ":none")
            }
        }
    }
}
