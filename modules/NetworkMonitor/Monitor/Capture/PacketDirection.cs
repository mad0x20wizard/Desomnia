namespace MadWizard.Desomnia.Network
{
    /// <summary>
    /// The direction of a captured packet relative to a watched host:
    /// <see cref="Inbound"/> — the host is the packet's destination,
    /// <see cref="Outbound"/> — the host is its source.
    /// </summary>
    public enum PacketDirection
    {
        Inbound,
        Outbound,
    }
}
