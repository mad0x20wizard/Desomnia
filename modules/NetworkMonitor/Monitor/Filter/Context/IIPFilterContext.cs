using System.Net;

namespace MadWizard.Desomnia.Network.Filter
{
    /// <summary>
    /// Identifies IP traffic suitable for IP predicates. Address resolution is excluded,
    /// including NDP even though it is carried in an IPv6 packet.
    /// </summary>
    public interface IIPFilterContext : IFilterContext;

    /// <summary>
    /// Transport metadata without a guarantee that any payload is available.
    /// A TCP SYN can provide this context.
    /// </summary>
    public interface IIPServiceFilterContext : IIPFilterContext
    {
        /// <summary>
        /// The watched host's service: destination port inbound, source port outbound.
        /// Its protocol identifies this traffic, not a configured TCP-or-UDP alternative.
        /// </summary>
        public IPService    Service         { get; }

        /// <summary>
        /// The peer's transport endpoint. Reuse the cached <see cref="IFilterContext.RemoteAddress"/>
        /// instance when constructing it, and cache the endpoint if it is created lazily.
        /// Port zero denotes an unavailable original peer port, such as in ForwardAuth.
        /// The endpoint is descriptive metadata, not a unique authorization-call identifier.
        /// </summary>
        public IPEndPoint   RemoteEndPoint  { get; }

        public IPProtocol   Protocol => Service.Protocol;
    }

    /// <summary>
    /// Base capability for generic payload predicates and specialized protocol contexts.
    /// It does not imply that enough data exists to interpret a complete application request.
    /// </summary>
    /// 
    /// <remarks>
    /// Outside a DemandRequest, the payload belongs to the current transport packet.
    /// Inside a DemandRequest, it is the reconstructed payload available for that connection
    /// and direction, with TCP gaps and retransmissions accounted for.
    /// Specialized contexts from another source, such as ForwardAuth, can supply interpreted
    /// metadata while throwing <see cref="NotSupportedException"/> for both raw-payload properties.
    /// </remarks>
    public interface IIPServicePayloadFilterContext : IIPServiceFilterContext
    {
        /// <summary>
        /// Available payload byte count. Reading this property must not materialize
        /// <see cref="PayloadData"/>. Traffic accounting must use the current packet's count,
        /// not repeatedly count an accumulated demand payload.
        /// </summary>
        /// 
        /// <exception cref="NotSupportedException">The source cannot provide raw payload.</exception>
        public int      PayloadLength   { get; }

        /// <summary>
        /// Available payload bytes, materialized lazily and reused. Consumers must not mutate
        /// the array. Already received body bytes ma
        /// y be present without being interpreted.
        /// </summary>
        /// <exception cref="NotSupportedException">The source cannot provide raw payload.</exception>
        public byte[]   PayloadData     { get; }
    }

    /// <summary>
    /// ICMP metadata normalized relative to the watched host.
    /// </summary>
    public interface IICMPFilterContext : IIPFilterContext
    {
        /// <summary>The logical ICMP operation, rather than its wire type number.</summary>
        ICMPType Type { get; }
    }

    public enum ICMPType
    {
        /// <summary>Traffic that has no supported direction-normalized operation.</summary>
        Unknown = 0,

        /// <summary>
        /// An inbound echo request or an outbound echo reply, for either IP version.
        /// An outbound echo request or inbound echo reply must not be classified as Echo.
        /// </summary>
        Echo
    }
}
