using Autofac;
using Autofac.Builder;
using MadWizard.Desomnia.Network.HyperV.Manager;
using MadWizard.Desomnia.Network.Manager;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MadWizard.Desomnia.Network.HyperV.Tests
{
    public class HyperVSwitchTests
    {
        [Theory]
        [InlineData("Microsoft:{b3b9fdd0-3a08-4322-9a6a-70dd9b20cda7}")]
        [InlineData("Microsoft:B3B9FDD0-3A08-4322-9A6A-70DD9B20CDA7")]
        [InlineData("{B3B9FDD0-3A08-4322-9A6A-70DD9B20CDA7}")]
        public void ConvertsHyperVDeviceIdToNetworkIdentity(string deviceId)
        {
            Assert.Equal(new NetworkIdentity("{B3B9FDD0-3A08-4322-9A6A-70DD9B20CDA7}"), HyperVSwitch.GetNetworkIdentity(deviceId));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("Hyper-V Virtual Ethernet Adapter")]
        [InlineData("Microsoft:{B3B9FDD0-3A08-4322-9A6A-70DD9B20CDA7}\\other-device")]
        public void DoesNotGuessAnIdentityFromOtherDeviceIds(string? deviceId)
        {
            Assert.Null(HyperVSwitch.GetNetworkIdentity(deviceId));
        }

        [Fact]
        public void PluginRegistersSwitchFactoryWithManager()
        {
            var builder = new ContainerBuilder();
            builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>));
            builder.RegisterModule(new PluginModule());
            using var container = builder.Build(ContainerBuildOptions.IgnoreStartableComponents);

            var createSwitch = container.Resolve<Func<Guid, string, HyperVSwitch>>();
            var guid = Guid.NewGuid();
            var @switch = createSwitch(guid, "External network");

            Assert.Equal(guid, @switch.GUID);
            Assert.Equal("External network", @switch.Name);
        }

        [Theory]
        [InlineData(@"Msvm_VirtualEthernetSwitch.Name=""B3B9FDD0-3A08-4322-9A6A-70DD9B20CDA7""")]
        [InlineData(@"\\HOST\root\virtualization\v2:Msvm_VirtualEthernetSwitch.CreationClassName=""Msvm_VirtualEthernetSwitch"",Name=""B3B9FDD0-3A08-4322-9A6A-70DD9B20CDA7""")]
        [InlineData(@"\\HOST\root\virtualization\v2:Msvm_VirtualEthernetSwitch.Name=""b3b9fdd0-3a08-4322-9a6a-70dd9b20cda7"",CreationClassName=""Msvm_VirtualEthernetSwitch""")]
        public void ReadsExactSwitchIdentityFromCimReference(string resource)
        {
            Assert.Equal(Guid.Parse("B3B9FDD0-3A08-4322-9A6A-70DD9B20CDA7"), HyperVSwitch.GetSwitchId(resource));
        }

        [Theory]
        [InlineData("")]
        [InlineData("External switch")]
        [InlineData(@"Msvm_VirtualEthernetSwitch.Name=""not-a-guid""")]
        [InlineData(@"Msvm_ComputerSystem.Name=""B3B9FDD0-3A08-4322-9A6A-70DD9B20CDA7""")]
        [InlineData(@"OtherMsvm_VirtualEthernetSwitch.Name=""B3B9FDD0-3A08-4322-9A6A-70DD9B20CDA7""")]
        [InlineData(@"Msvm_VirtualEthernetSwitch.Name=""B3B9FDD0-3A08-4322-9A6A-70DD9B20CDA7-suffix""")]
        public void DoesNotInferSwitchFromAnUnrelatedReference(string resource)
        {
            Assert.Null(HyperVSwitch.GetSwitchId(resource));
        }
    }
}
