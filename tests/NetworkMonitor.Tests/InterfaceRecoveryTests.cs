using Xunit;
using InterfaceRecovery = MadWizard.Desomnia.Network.Interface.InterfaceConfigurator.InterfaceRecovery;

namespace MadWizard.Desomnia.Network.Tests;

public class InterfaceRecoveryTests
{
    [Fact]
    public async Task WaitsForUsableStateAndRetriesFailedCaptureOpen()
    {
        int reads = 0, opens = 0;
        await InterfaceRecovery.Wait(() => new(++reads >= 3, "ready"),
            () => { if (++opens == 1) throw new InvalidOperationException("still restarting"); },
            TimeSpan.FromSeconds(3), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, opens);
    }

    [Fact]
    public async Task DoesNotRequireDownTransitionOnNonRestartingPlatforms()
    {
        int opens = 0;
        await InterfaceRecovery.Wait(() => new(true, "ready"), () => opens++,
            TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, opens);
    }

    [Fact]
    public async Task PersistentDisconnectionTimesOutWithoutOpeningCapture()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => InterfaceRecovery.Wait(() => new(false, "down"),
            () => throw new Xunit.Sdk.XunitException("Must not reopen a down interface"),
            TimeSpan.FromMilliseconds(30), TimeSpan.Zero, TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public async Task CaptureThatStopsAfterOpenIsRetried()
    {
        int opens = 0;
        await InterfaceRecovery.Wait(() => new(true, "up"), () => opens++,
            TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.FromMilliseconds(1), () => opens >= 2);
        Assert.Equal(2, opens);
    }

    [Fact]
    public async Task ChangeDuringCaptureOpenRequiresAnotherRecovery()
    {
        int reads = 0, opens = 0;
        await InterfaceRecovery.Wait(() => ++reads == 2 ? new(false, "down") : new(true, "up"),
            () => opens++, TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, opens);
    }
}
