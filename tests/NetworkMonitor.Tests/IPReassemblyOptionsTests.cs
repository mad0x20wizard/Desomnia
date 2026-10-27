using MadWizard.Desomnia.Network.Configuration.Options;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests;

public class IPReassemblyOptionsTests
{
    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(-1)]
    public void InvalidFamilyThrows(int flags)
    {
        Assert.Throws<ArgumentOutOfRangeException>(nameof(ReassemblyOptions.Protocol),
            () => new ReassemblyOptions { Protocol = (ReassemblyProtocol)flags });
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void NonPositiveTimeoutThrows(long ticks)
    {
        Assert.Throws<ArgumentOutOfRangeException>(nameof(ReassemblyOptions.Timeout),
            () => new ReassemblyOptions { Timeout = TimeSpan.FromTicks(ticks) });
    }
}
