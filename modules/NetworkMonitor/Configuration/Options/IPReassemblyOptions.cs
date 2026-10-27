namespace MadWizard.Desomnia.Network.Configuration.Options;

[Flags]
public enum IPReassemblyFamily
{
    None = 0,

    IPv4 = 1,
    IPv6 = 2,

    /// <summary>Pass selected families' fragments through without reassembling them.</summary>
    Passthrough = 4,
}

/// <summary>Process-wide settings read from root configuration at startup.</summary>
public sealed class IPReassemblyOptions
{
    public IPReassemblyFamily Family
    {
        get; set
        {
            if ((value & ~(IPReassemblyFamily.IPv4 | IPReassemblyFamily.IPv6 | IPReassemblyFamily.Passthrough)) != 0)
                throw new ArgumentOutOfRangeException(nameof(Family), "Unknown IP reassembly family flags.");

            field = value;
        }
    } = IPReassemblyFamily.None; // IPReassemblyFamily.IPv4 | IPReassemblyFamily.IPv6; // TODO enable by default?

    public TimeSpan Timeout
    {
        get; set
        {
            if (value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(Timeout), "The reassembly timeout must be positive.");

            field = value;
        }
    } = TimeSpan.FromSeconds(60);
}
