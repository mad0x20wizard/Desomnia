$hyperv = Get-WindowsOptionalFeature -FeatureName Microsoft-Hyper-V-All -Online

if($hyperv.State -eq "Enabled") # Check if Hyper-V is enabled
{
    "HyperV" | Out-File -FilePath "virtual_support.txt" -Encoding UTF8
} 
else 
{
    "" | Out-File -FilePath "virtual_support.txt" -Encoding UTF8
}