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
        public async Task ExpirationRemovesTheRangeAndRaisesOneEvent()
        {
            using var ranges = new NetworkHostRange();
            var range = IPAddressRange.Parse("192.0.2.0/24");
            var removed = new TaskCompletionSource<IPAddressRange>(TaskCreationOptions.RunContinuationsAsynchronously);
            int removals = 0;
            ranges.AddressRangeRemoved += (_, value) => { Interlocked.Increment(ref removals); removed.TrySetResult(value); };

            Assert.True(ranges.AddAddressRange(range, new(TimeSpan.FromMilliseconds(100))));
            Assert.Equal(range, await removed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(ranges.Contains(Peer));
            Assert.Empty(ranges);
            Assert.False(ranges.RemoveAddressRange(range));
            Assert.Equal(1, removals);
        }

        [Fact]
        public async Task RenewalSurvivesTheOriginalDeadlineWithoutDuplicateAddition()
        {
            using var ranges = new NetworkHostRange();
            var range = new IPAddressRange(Peer);
            int additions = 0;
            ranges.AddressRangeAdded += (_, _) => additions++;
            ranges.AddAddressRange(range, new(TimeSpan.FromMilliseconds(200)));
            var extended = DateTime.Now.AddMinutes(1);

            Assert.False(ranges.AddAddressRange(range, new() { Expires = extended }));
            Assert.False(ranges.AddAddressRange(range, new(TimeSpan.FromSeconds(1))));
            await Task.Delay(400);
            ranges.RemoveExpiredAddressRanges(); // a stale cleanup must consult the renewed deadline

            Assert.True(ranges.Contains(Peer));
            Assert.True(ranges.ShouldAddressRangeExpire(range, out var expires));
            Assert.Equal(extended, expires);
            Assert.Equal(1, additions);
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
            ranges.RemoveExpiredAddressRanges();

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
        public void LongLeasesAreSupportedAndExpiredGrantsCannotAuthorize()
        {
            using var ranges = new NetworkHostRange();
            ranges.AddAddress(Peer, new(TimeSpan.FromDays(365)));
            Assert.True(ranges.Contains(Peer));
            ranges.RemoveAddress(Peer);
            ranges.AddAddress(Peer, new() { Expires = DateTime.Now.AddSeconds(-1) });
            Assert.False(ranges.Contains(Peer));
        }

        [Fact]
        public async Task DisposalCancelsPendingExpiration()
        {
            var ranges = new NetworkHostRange();
            bool removed = false;
            ranges.AddressRangeRemoved += (_, _) => removed = true;
            ranges.AddAddress(Peer, new(TimeSpan.FromMilliseconds(200)));
            ((IDisposable)ranges).Dispose();
            await Task.Delay(400);

            Assert.False(removed);
            Assert.Throws<ObjectDisposedException>(() => ranges.AddAddress(Peer));
        }
    }
}
