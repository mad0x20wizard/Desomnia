using Autofac;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Firewall.Manager;
using MadWizard.Desomnia.Firewall.WindowsFirewall.Native;
using NetTools;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using Xunit;

namespace MadWizard.Desomnia.Firewall.WindowsFirewall.Tests
{
    public sealed class WfpFactAttribute : FactAttribute
    {
        public WfpFactAttribute()
        {
            if (!OperatingSystem.IsWindows())
                Skip = "Requires Windows WFP.";
            else
            {
                using var identity = WindowsIdentity.GetCurrent();
                if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                    Skip = "Requires an elevated test process to install temporary loopback-only WFP filters.";
            }
        }
    }

    public class WfpTests
    {
        [Fact]
        public void PluginRegistersOnlyThePlatformBackend()
        {
            var builder = new ContainerBuilder();
            builder.RegisterModule<PluginModule>();
            using var container = builder.Build();

            Assert.IsType<Manager.WindowsFirewallManager>(Assert.Single(container.Resolve<IEnumerable<IFirewallManager>>()));
            Assert.Empty(container.Resolve<IEnumerable<INetworkService>>());
        }

        [WfpFact]
        public async Task PermitOverridesFallbackDenyOverridesPermitAndClosingSessionRestoresAccess()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            ushort port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
            var peer = IPAddress.Loopback;
            Assert.True(await Connect(peer, port));

            var any = IPAddressRange.Parse("0.0.0.0/0");
            FirewallRule fallback = new(peer, new(IPProtocol.TCP, port), any, FirewallAction.Block, FirewallPriority.Default);
            FirewallRule permit = new(peer, new(IPProtocol.TCP, port), new(peer), FirewallAction.Permit, FirewallPriority.Permit);
            FirewallRule deny = permit with { Action = FirewallAction.Block, Priority = FirewallPriority.Deny };

            using var control = new TcpListener(IPAddress.Loopback, 0);
            control.Start();
            ushort controlPort = (ushort)((IPEndPoint)control.LocalEndpoint).Port;
            using (WfpSession.Install("isolated TCP test", [fallback]))
            {
                Assert.False(await Connect(peer, port));
                Assert.True(await Connect(peer, controlPort));
            }

            using (WfpSession.Install("isolated TCP test", [deny]))
            {
                Assert.False(await Connect(peer, port));
                Assert.True(await Connect(IPAddress.Parse("127.0.0.2"), port));
            }

            using (WfpSession.Install("isolated TCP test", [fallback, permit]))
            {
                Assert.True(await Connect(peer, port));
                Assert.False(await Connect(IPAddress.Parse("127.0.0.2"), port));
            }

            using (WfpSession.Install("isolated TCP test", [fallback, permit, deny]))
                Assert.False(await Connect(peer, port));

            Assert.True(await Connect(peer, port));
        }

        [WfpFact]
        public async Task IPv6SourceRangeIsInstalledAndRemoved()
        {
            if (!Socket.OSSupportsIPv6)
                return;
            using var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
            listener.Start();
            ushort port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
            var peer = IPAddress.IPv6Loopback;
            FirewallRule fallback = new(peer, new(IPProtocol.TCP, port), IPAddressRange.Parse("::/0"), FirewallAction.Block, FirewallPriority.Default);
            FirewallRule permit = new(peer, new(IPProtocol.TCP, port), new(peer), FirewallAction.Permit, FirewallPriority.Permit);
            Assert.True(await Connect(peer, port));
            using (WfpSession.Install("isolated IPv6 test", [fallback]))
                Assert.False(await Connect(peer, port));
            using (WfpSession.Install("isolated IPv6 test", [fallback, permit]))
                Assert.True(await Connect(peer, port));
            Assert.True(await Connect(peer, port));
        }

        [WfpFact]
        public async Task UdpAuthorizationUsesSourceAndServicePort()
        {
            using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            ushort port = (ushort)((IPEndPoint)listener.Client.LocalEndPoint!).Port;
            FirewallRule fallback = new(IPAddress.Loopback, new(IPProtocol.UDP, port),
                IPAddressRange.Parse("0.0.0.0/0"), FirewallAction.Block, FirewallPriority.Default);
            FirewallRule permit = fallback with { Source = new(IPAddress.Loopback), Action = FirewallAction.Permit, Priority = FirewallPriority.Permit };
            Assert.True(await Send(listener, IPAddress.Loopback, port));
            using (WfpSession.Install("isolated UDP test", [fallback, permit]))
            {
                Assert.True(await Send(listener, IPAddress.Loopback, port));
                Assert.False(await Send(listener, IPAddress.Parse("127.0.0.2"), port));
            }
            Assert.True(await Send(listener, IPAddress.Parse("127.0.0.2"), port));
        }

        private static async Task<bool> Connect(IPAddress source, ushort port)
        {
            using var client = new TcpClient(source.AddressFamily);
            client.Client.Bind(new IPEndPoint(source, 0));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await client.ConnectAsync(source.AddressFamily == AddressFamily.InterNetwork
                    ? IPAddress.Loopback : IPAddress.IPv6Loopback, port, timeout.Token);
                return true;
            }
            catch (SocketException) { return false; }
            catch (OperationCanceledException) { return false; }
        }

        private static async Task<bool> Send(UdpClient listener, IPAddress source, ushort port)
        {
            using var client = new UdpClient(new IPEndPoint(source, 0));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            byte[] message = Guid.NewGuid().ToByteArray();
            try
            {
                await client.SendAsync(message, new IPEndPoint(IPAddress.Loopback, port), timeout.Token);
                var result = await listener.ReceiveAsync(timeout.Token);
                return result.Buffer.SequenceEqual(message);
            }
            catch (SocketException) { return false; }
            catch (OperationCanceledException) { return false; }
        }
    }
}
