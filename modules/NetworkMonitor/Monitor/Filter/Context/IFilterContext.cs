using System.Net;

namespace MadWizard.Desomnia.Network.Filter
{
    /// <summary>
    /// Metadata for one filter evaluation, relative to the watched host.
    /// This base context also represents address resolution without implying that
    /// IP unicast traffic or a transport service is available.
    /// </summary>
    /// 
    /// <remarks>
    /// Contexts are snapshots of the information available for an evaluation.
    /// Consumers must treat exposed reference values as immutable.
    /// </remarks>
    public interface IFilterContext
    {
        public FlowDirection Direction { get; }

        /// <summary>
        /// The peer's address: source for inbound traffic, destination for outbound traffic.
        /// This is not the watched host's address.
        /// </summary>
        public IPAddress RemoteAddress { get; }
    }

    /// <summary>
    /// The direction of traffic relative to a watched host:
    /// <see cref="Inbound"/> — the host is the packet's destination,
    /// <see cref="Outbound"/> — the host is its source.
    /// </summary>
    public enum FlowDirection
    {
        Inbound,
        Outbound,
    }
}
