using System.Net;

namespace MadWizard.Desomnia.Network.Filter
{
    public interface IFilterContext
    {
        public FlowDirection Direction      { get; }

        public IPEndPoint RemoteEndPoint    { get; }
    }

    /// <summary>
    /// The direction of a captured packet relative to a watched host:
    /// <see cref="Inbound"/> — the host is the packet's destination,
    /// <see cref="Outbound"/> — the host is its source.
    /// </summary>
    public enum FlowDirection
    {
        Inbound,
        Outbound,
    }
}
