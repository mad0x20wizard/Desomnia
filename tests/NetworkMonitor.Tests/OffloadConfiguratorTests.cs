using MadWizard.Desomnia.Network.Interface.Configuration;
using MadWizard.Desomnia.Network.Manager;
using Xunit;
using Fixture = MadWizard.Desomnia.Network.Tests.ReconfigurationLifecycleTests.Fixture;

namespace MadWizard.Desomnia.Network.Tests
{
    public class OffloadConfiguratorTests
    {
        [Fact]
        public async Task RepeatedSuspendsPreserveOriginalSettingsForShutdown()
        {
            var manager = new OffloadManager { Current = OffloadProtocol.IP };
            using var fixture = new Fixture();
            var service = Create(fixture, OffloadProtocol.None, manager);

            await service.BeforeSuspend();
            Assert.Equal(OffloadProtocol.None, manager.Current);
            await service.BeforeSuspend();
            Assert.Single(manager.Writes);

            // A driver may restore settings independently between suspend cycles.
            manager.Current = OffloadProtocol.IPv4;
            await service.BeforeSuspend();
            Assert.Equal(OffloadProtocol.None, manager.Current);

            await service.Shutdown(NetworkShutdownReason.ApplicationShutdown);
            Assert.Equal(OffloadProtocol.IP, manager.Current);
            Assert.Equal(new[] { OffloadProtocol.None, OffloadProtocol.None, OffloadProtocol.IP }, manager.Writes);

            await service.Shutdown(NetworkShutdownReason.ApplicationShutdown);
            Assert.Equal(3, manager.Writes.Count);
        }

        [Fact]
        public async Task PartialConfigurationFailureStillRestoresOriginalSettings()
        {
            var manager = new OffloadManager { Current = OffloadProtocol.IP, FailNextWrite = true };
            using var fixture = new Fixture();
            var service = Create(fixture, OffloadProtocol.None, manager);

            await service.BeforeSuspend();
            Assert.Equal(OffloadProtocol.IPv6, manager.Current);

            await service.Shutdown(NetworkShutdownReason.InterfaceShutdown);
            Assert.Equal(OffloadProtocol.IP, manager.Current);
        }

        [Theory]
        [InlineData(OffloadProtocol.None, OffloadProtocol.IPv4, OffloadProtocol.IP)]
        [InlineData(OffloadProtocol.IPv4, OffloadProtocol.IPv4, OffloadProtocol.IPv4)]
        [InlineData(OffloadProtocol.None, OffloadProtocol.None, OffloadProtocol.None)]
        public async Task UnsupportedOrUnchangedRequestsLeaveOriginalValue(
            OffloadProtocol current, OffloadProtocol supported, OffloadProtocol requested)
        {
            var manager = new OffloadManager { Current = current, SupportedProtocols = supported };
            using var fixture = new Fixture();
            var service = Create(fixture, requested, manager);

            await service.BeforeSuspend();
            await service.Shutdown(NetworkShutdownReason.ApplicationShutdown);

            Assert.Equal(current, manager.Current);
            Assert.All(manager.Writes, value => Assert.Equal(current, value));
        }

        [Fact]
        public async Task MissingManagerDoesNotInterruptLifecycle()
        {
            using var fixture = new Fixture();
            var service = Create(fixture, OffloadProtocol.None, null);

            await service.BeforeSuspend();
            await service.Shutdown(NetworkShutdownReason.ApplicationShutdown);
        }

        private static INetworkService Create(Fixture fixture, OffloadProtocol offload, IProtocolOffloadManager? manager)
            => InterfaceReconfigurationTests.Create(fixture, new OffloadConfigurator(offload, manager));
        private sealed class OffloadManager : IProtocolOffloadManager
        {
            public OffloadProtocol SupportedProtocols { get; init; } = OffloadProtocol.IP;
            public OffloadProtocol Current { get; set; }
            public bool FailNextWrite { get; set; }
            public List<OffloadProtocol> Writes { get; } = [];

            public OffloadProtocol OffloadProtocols
            {
                get => Current;
                set
                {
                    Writes.Add(value);
                    if (FailNextWrite)
                    {
                        FailNextWrite = false;
                        Current &= ~OffloadProtocol.IPv4;
                        throw new InvalidOperationException("NS configuration failed after ARP was disabled.");
                    }

                    Current = value;
                }
            }
        }
    }
}
