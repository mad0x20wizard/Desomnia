using MadWizard.Desomnia.Service;
using MadWizard.Desomnia.Service.Duo;
using System.ServiceProcess;
using Xunit;
using static DuoStreamIntegration.Tests.DuoTestSupport;

namespace DuoStreamIntegration.Tests;

public sealed class WindowsMocksTests
{
    [Fact]
    public void Concrete_services_have_independent_settings_and_real_managed_subscriptions()
    {
        var first = new FakeDuoService { Settings = new DuoSettings { Port = 1111, Instances = [Settings("Alpha")] } };
        var second = new FakeDuoService { Settings = new DuoSettings { Port = 2222, Instances = [Settings("Beta")] } };
        Assert.IsType<DuoService>(first.Service);
        Assert.IsType<DuoService>(second.Service);
        Assert.Equal(1111u, first.Service.Settings.Port);
        Assert.Equal(2222u, second.Service.Settings.Port);
        Assert.Equal(first.PID, first.Service.PID);
        Assert.Equal(first.ExecutablePath, first.Service.ExecutablePath);
        Assert.Equal(first.Version, first.Service.Version);

        var notifications = 0;
        EventHandler<MadWizard.Desomnia.Service.Controller.ServiceStatusChangedEventArgs> handler = (_, _) => notifications++;
        first.Service.StatusChanged += handler;
        first.Publish(ServiceControllerStatus.Stopped);
        Assert.Equal(1, notifications);
        Assert.Equal(ServiceControllerStatus.Stopped, first.Service.ObservedStatus);
        Assert.Equal(ServiceControllerStatus.Running, second.Service.ObservedStatus);
        first.Service.StatusChanged -= handler;
        first.Publish(ServiceControllerStatus.Running);
        Assert.Equal(1, notifications);
    }
}
