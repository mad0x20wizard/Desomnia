namespace MadWizard.Desomnia.Network.Configuration.Options;

[Flags]
public enum ReassemblyProtocol
{
    None = 0,

    IPv4 = 1,
    IPv6 = 2,

    /// <summary>Pass selected families' fragments through without reassembling them.</summary>
    Passthrough = 4,
}

/// <summary>Capture reassembly settings for one network monitor.</summary>
public readonly struct ReassemblyOptions
{
    public ReassemblyProtocol Protocol
    {
        get; init
        {
            if ((value & ~(ReassemblyProtocol.IPv4 | ReassemblyProtocol.IPv6 | ReassemblyProtocol.Passthrough)) != 0)
                throw new ArgumentOutOfRangeException(nameof(Protocol), "Unknown reassembly protocol flags.");

            field = value;
        }
    }

    public TimeSpan Timeout
    {
        get; init
        {
            if (value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(Timeout), "The reassembly timeout must be positive.");

            field = value;
        }
    }
}
