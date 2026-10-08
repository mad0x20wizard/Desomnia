using Autofac.Features.OwnedInstances;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Naming.Options;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.SleepProxy.Registration;
using MadWizard.Desomnia.Network.Watch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests
{
    public class SleepProxyRegistrationFailureTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task FailedSetupReleasesLeaseAndPartialResources(bool canceled)
        {
            var network = new NetworkSegment
            {
                Logger = NullLogger<NetworkSegment>.Instance,
                Device = null!, LocalRange = null!, Ranges = null!
            };
            // Setup can fail before the registration's MAC has been applied to the host.
            var host = new NetworkHost("configured") { Network = network };
            var configuredIP = IPAddress.Parse("192.0.2.10");
            var leasedIP = IPAddress.Parse("192.0.2.11");
            host.AddAddress(configuredIP);
            host.AddAddress(leasedIP);
            network.AddHost(host);

            var watch = new RemoteHostWatch
            {
                Host = host, Network = network,
                Logger = NullLogger<NetworkHostWatch>.Instance,
                Scope = null!, Device = null!, Filter = null!,
                AdvertiseOptions = default, HandoffOptions = default,
                DemandOptions = default, DefaultFilterOptions = default,
                Reachability = null!, ReachabilityCache = null!,
                AddressMapping = null!, Knocking = null!,
                PingOptions = default, WakeOptions = default
            };
            var logger = new RecordingLogger();
            var registrar = new SleepProxyRegistrar(AutoDiscoveryType.Everything, default)
            {
                Logger = logger, Context = null!, Reachability = null!, CreateLease = null!
            };
            var lease = CreateLease("001122334455");
            using var owned = new Owned<SleepProxyLease>(lease, lease);
            lease.AddInstanceForDisposal(new SleepProxyAddressLease(NullLogger.Instance, host, leasedIP));
            bool disposed = false;
            lease.Disposed += (_, _) => disposed = true;

            var otherLease = CreateLease("001122334466");
            using var otherOwned = new Owned<SleepProxyLease>(otherLease, otherLease);
            var active = (ConcurrentDictionary<PhysicalAddress, Owned<SleepProxyLease>>)
                typeof(SleepProxyRegistrar).GetField("_activeLeases", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(registrar)!;
            active[lease.Registration.PhysicalAddress] = owned;
            active[otherLease.Registration.PhysicalAddress] = otherOwned;

            var error = new InvalidOperationException("Watch setup failed");
            var setup = canceled
                ? Task.FromCanceled<SleepProxyLease>(new CancellationToken(true))
                : Task.FromException<SleepProxyLease>(error);

            Task completion;
            using (await network.Mutex.LockAsync())
            {
                completion = (Task)typeof(SleepProxyRegistrar)
                    .GetMethod("FinishRegistration", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(registrar, [watch, owned, setup])!;

                // Teardown must await the network mutex before removing host resources.
                Assert.False(completion.IsCompleted);
                Assert.False(disposed);
                Assert.True(host.HasAddress(ip: leasedIP));
            }

            await completion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(disposed);
            Assert.False(active.ContainsKey(lease.Registration.PhysicalAddress));
            Assert.Same(otherOwned, Assert.Single(active).Value);
            Assert.False(host.HasAddress(ip: leasedIP));
            Assert.True(host.HasAddress(ip: configuredIP));
            Assert.Same(host, network[host.Name]);
            Assert.Null(host.PhysicalAddress);

            var logged = Assert.Single(logger.Exceptions);
            if (canceled)
                Assert.IsAssignableFrom<OperationCanceledException>(logged);
            else
                Assert.Same(error, logged);
        }

        private static SleepProxyLease CreateLease(string mac) => new(TimeSpan.FromHours(1))
        {
            Registration = new SleepProxyRegistration("registered", "registered",
                new EdnsOwnerOption { PrimaryMac = PhysicalAddress.Parse(mac) },
                new EdnsLeaseOption { Duration = TimeSpan.FromHours(1) })
        };

        private sealed class RecordingLogger : ILogger<SleepProxyRegistrar>
        {
            public List<Exception> Exceptions { get; } = [];

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (exception is not null)
                    Exceptions.Add(exception);
            }
        }
    }
}
