using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Neighborhood.Options;
using NetTools;
using System.Net;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests
{
    public class NetworkHostRangeTests
    {
        private static readonly IPAddress Peer = IPAddress.Parse("192.0.2.10");

        [Fact]
        public void ExpirationIsMetadataUntilTheJanitorRemovesTheEntry()
        {
            using var ranges = new NetworkHostRange();
            var range = IPAddressRange.Parse("192.0.2.0/24");
            var expires = DateTime.Now.AddHours(-1);
            ranges.AddAddressRange(range, new() { Expires = expires });

            Assert.True(ranges.Contains(Peer));
            Assert.Equal(range, Assert.Single(ranges));
            Assert.True(ranges.ShouldAddressRangeExpire(range, out var actual));
            Assert.Equal(expires, actual);
        }

        [Fact]
        public void RenewalRaisesCleanupChangedWithoutDuplicateAddition()
        {
            using var ranges = new NetworkHostRange();
            var range = new IPAddressRange(Peer);
            int additions = 0, changes = 0;
            ranges.AddressRangeAdded += (_, _) => additions++;
            ranges.Changed += (_, _) => changes++;
            ranges.AddAddressRange(range, new(TimeSpan.FromSeconds(1)));
            var extended = DateTime.Now.AddMinutes(1);

            Assert.False(ranges.AddAddressRange(range, new() { Expires = extended }));
            Assert.False(ranges.AddAddressRange(range, new(TimeSpan.FromSeconds(2))));

            Assert.True(ranges.Contains(Peer));
            Assert.Equal(extended, ranges[range].Expires);
            Assert.Equal(1, additions);
            Assert.Equal(2, changes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PermanentEntriesAreNotTurnedIntoTemporaryGrants(bool isStatic)
        {
            using var ranges = new NetworkHostRange();
            var range = new IPAddressRange(Peer);
            ranges.AddAddressRange(range, new(isStatic ? IPAddressRangeFlags.Static : IPAddressRangeFlags.None));
            ranges.AddAddressRange(range, new() { Expires = DateTime.Now.AddDays(-1) });

            Assert.True(ranges.Contains(Peer));
            Assert.False(ranges.ShouldAddressRangeExpire(range, out _));
        }

        [Fact]
        public void PermanentRegistrationCanReplaceATemporaryGrant()
        {
            using var ranges = new NetworkHostRange();
            var range = new IPAddressRange(Peer);
            ranges.AddAddressRange(range, new(TimeSpan.FromHours(1)));
            ranges.AddAddressRange(range, new(IPAddressRangeFlags.Static));

            Assert.True(ranges[range].HasFlags(IPAddressRangeFlags.Static));
            Assert.False(ranges.ShouldAddressRangeExpire(range, out _));
        }

        [Fact]
        public void LongLeasesAndExpiredEntriesAreRetained()
        {
            using var ranges = new NetworkHostRange();
            ranges.AddAddress(Peer, new(TimeSpan.FromDays(365)));
            Assert.True(ranges.Contains(Peer));
            ranges.RemoveAddress(Peer);
            ranges.AddAddress(Peer, new() { Expires = DateTime.Now.AddSeconds(-1) });
            Assert.True(ranges.Contains(Peer));
        }

        [Fact]
        public void EnumerationRetainsASnapshotAcrossMutations()
        {
            using var ranges = new NetworkHostRange();
            ranges.AddAddress(Peer);
            using var snapshot = ranges.GetEnumerator();
            ranges.RemoveAddress(Peer);
            ranges.AddAddress(IPAddress.Parse("192.0.2.11"));

            Assert.True(snapshot.MoveNext());
            Assert.Equal(Peer, snapshot.Current.Begin);
            Assert.False(snapshot.MoveNext());
            Assert.False(ranges.Contains(Peer));
        }

        [Fact]
        public void CleanupCannotRemoveARenewedOrPermanentEntryUsingAnOldExpiration()
        {
            using var ranges = new NetworkHostRange();
            var range = new IPAddressRange(Peer);
            var original = DateTime.Now.AddMinutes(-1);
            var renewed = DateTime.Now.AddMinutes(1);
            ranges.AddAddressRange(range, new() { Expires = original });
            ranges.AddAddressRange(range, new() { Expires = renewed });

            Assert.False(ranges.RemoveAddressRange(range, original));
            Assert.True(ranges.Contains(Peer));
            ranges.AddAddressRange(range, new(IPAddressRangeFlags.Static));
            Assert.False(ranges.RemoveAddressRange(range, renewed));
            Assert.True(ranges.Contains(Peer));

            ranges.RemoveAddressRange(range);
            ranges.AddAddressRange(range, new() { Expires = original });
            Assert.True(ranges.RemoveAddressRange(range, original));
            Assert.Empty(ranges);
        }

        [Fact]
        public async Task ReadsAndMutationsDoNotRequireTheNetworkMutex()
        {
            using var ranges = new NetworkHostRange();
            var range = new IPAddressRange(Peer);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var writer = Task.Run(async () =>
            {
                await start.Task;
                for (int i = 0; i < 2000; i++)
                {
                    ranges.AddAddressRange(range, new(TimeSpan.FromMinutes(1)));
                    ranges.AddAddressRange(range, new(TimeSpan.FromMinutes(2)));
                    ranges.RemoveAddress(Peer);
                }
            });
            var readers = Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
            {
                await start.Task;
                for (int i = 0; i < 2000; i++)
                {
                    ranges.Contains(Peer);
                    ranges.ShouldAddressRangeExpire(range, out var expires);
                    foreach (var entry in ranges)
                        Assert.Equal(range, entry);
                }
            })).ToArray();

            start.SetResult();
            await Task.WhenAll(readers.Append(writer)).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Empty(ranges);
        }

        [Fact]
        public void NotificationsAllowAnotherThreadToReadTheRange()
        {
            using var ranges = new NetworkHostRange();
            var host = new NetworkHost("linked") { Network = null! };
            var range = new IPAddressRange(Peer);
            var second = IPAddress.Parse("192.0.2.11");
            int notifications = 0;
            void ReadOnAnotherThread(object? sender, EventArgs args)
            {
                Task.Run(() => ranges.Contains(Peer)).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                notifications++;
            }
            ranges.Changed += ReadOnAnotherThread;
            ranges.AddressRangeAdded += ReadOnAnotherThread;
            ranges.AddressRangeRemoved += ReadOnAnotherThread;

            ranges.AddAddressRange(range, new(TimeSpan.FromMinutes(1)));
            ranges.AddAddressRange(range, new(TimeSpan.FromMinutes(2)));
            ranges.RemoveAddressRange(range);
            host.AddAddress(Peer);
            Assert.True(ranges.AddHostAddressRange(host));
            host.AddAddress(second);
            Assert.True(ranges.Contains(second));
            host.RemoveAddress(second);
            Assert.False(ranges.Contains(second));
            Assert.True(ranges.RemoveHostAddressRange(host));
            Assert.False(ranges.Contains(Peer));
            host.AddAddress(second);
            Assert.False(ranges.Contains(second));
            ranges.AddHostAddressRange(host);

            // Final disposal intentionally publishes removals while holding the range lock.
            ranges.Changed -= ReadOnAnotherThread;
            ranges.AddressRangeAdded -= ReadOnAnotherThread;
            ranges.AddressRangeRemoved -= ReadOnAnotherThread;
            ((IDisposable)ranges).Dispose();
            Assert.Empty(ranges);
            Assert.True(notifications > 0);
        }

        [Fact]
        public void DisposalPreventsNewEntries()
        {
            var ranges = new NetworkHostRange();
            bool removed = false;
            ranges.AddressRangeRemoved += (_, _) => removed = true;
            ranges.AddAddress(Peer, new(TimeSpan.FromMilliseconds(200)));
            ((IDisposable)ranges).Dispose();

            Assert.False(removed);
            Assert.Throws<ObjectDisposedException>(() => ranges.AddAddress(Peer));
        }
    }
}
