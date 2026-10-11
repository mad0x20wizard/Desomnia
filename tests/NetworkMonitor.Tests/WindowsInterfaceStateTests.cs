using MadWizard.Desomnia.Network.Manager;
using System.Net.NetworkInformation;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests;

public class WindowsInterfaceStateTests
{
    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows()) Skip = "Requires Windows IP Helper.";
        }
    }

    [WindowsFact]
    public void LoopbackAdministrativeStateIsReadableWithoutACimAdapter()
    {
        var loopback = NetworkInterface.GetAllNetworkInterfaces().First(nic => nic.NetworkInterfaceType == NetworkInterfaceType.Loopback);
        Assert.Contains(Guid.Parse(loopback.Id), NetworkInterfaceInterop.GetIPInterfaceIds());
        Assert.True(NetworkInterfaceInterop.TryGetDisabled(Guid.Parse(loopback.Id), out bool disabled));
        Assert.False(disabled);
    }

    [WindowsFact]
    public void UnknownInterfaceAllowsTheCimFallback()
    {
        Assert.False(NetworkInterfaceInterop.TryGetDisabled(Guid.NewGuid(), out _));
    }

    [WindowsFact]
    public void PresentIpBoundInterfacesHaveAdministrativeState()
    {
        var ids = NetworkInterfaceInterop.GetIPInterfaceIds();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(nic => ids.Contains(Guid.Parse(nic.Id))))
        {
            // Recheck membership if a real adapter disappears during this read-only test.
            if (NetworkInterfaceInterop.TryGetDisabled(Guid.Parse(nic.Id), out _)) continue;
            Assert.DoesNotContain(NetworkInterface.GetAllNetworkInterfaces(), current => current.Id == nic.Id);
        }
    }
}
