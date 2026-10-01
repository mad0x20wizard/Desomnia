using MadWizard.Desomnia.Service.Duo;
using Xunit;

namespace DuoStreamIntegration.Tests;

public sealed class DuoServiceSettingsTests
{
    [Theory]
    [InlineData(null, 38299)]
    [InlineData(38300, 38300)]
    public void Settings_reads_the_service_port_and_instance_settings_from_their_respective_keys(int? port, int expectedPort)
    {
        using var registry = new TestDuoRegistry();
        if (port is int configuredPort) registry.Key.SetValue("Port", configuredPort);
        using var instance = registry.Key.CreateSubKey(@"Instances\Player");
        instance.SetValue("DisplayName", "Player display name");
        instance.SetValue("Port", 47989);
        instance.SetValue("UserName", "player");
        instance.SetValue("Sandboxed", 1);

        // Keep SCM inactive but let the real Settings getter read the test registry.
        var service = new FakeDuoService().Service;
        WindowsMocks.Services.Remove(service);
        var settings = service.Settings;

        Assert.Equal((uint)expectedPort, settings.Port);
        var player = Assert.Single(settings.Instances);
        Assert.Equal("Player", player.Name);
        Assert.Equal("Player display name", player.DisplayName);
        Assert.Equal(47989, player.Port);
        Assert.Equal("player", player.UserName);
        Assert.True(player.IsSandboxed);
    }
}
