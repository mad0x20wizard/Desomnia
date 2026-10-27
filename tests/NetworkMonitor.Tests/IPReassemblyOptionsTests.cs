using MadWizard.Desomnia.Network.Configuration.Options;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests;

public class IPReassemblyOptionsTests
{
    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(-1)]
    public void InvalidFamilyAssignmentThrowsAndPreservesPreviousValue(int flags)
    {
        var family = IPReassemblyFamily.IPv6 | IPReassemblyFamily.Passthrough;
        var options = new IPReassemblyOptions { Family = family };

        Assert.Throws<ArgumentOutOfRangeException>(nameof(IPReassemblyOptions.Family),
            () => options.Family = (IPReassemblyFamily)flags);

        Assert.Equal(family, options.Family);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void NonPositiveTimeoutAssignmentThrowsAndPreservesPreviousValue(long ticks)
    {
        var timeout = TimeSpan.FromTicks(1);
        var options = new IPReassemblyOptions { Timeout = timeout };

        Assert.Throws<ArgumentOutOfRangeException>(nameof(IPReassemblyOptions.Timeout),
            () => options.Timeout = TimeSpan.FromTicks(ticks));

        Assert.Equal(timeout, options.Timeout);
    }
}
