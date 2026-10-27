using MadWizard.Desomnia.Network.Filter;

namespace MadWizard.Desomnia.Network.Capture
{
    /// <summary>
    /// Stateless factory for capture-derived filter contexts. The selected host or service
    /// scope supplies the implementation; a DemandRequest resolves it through its own scope.
    /// </summary>
    /// <remarks>
    /// Each call creates a fresh evaluation snapshot with the most specialized context
    /// supported by the available information. Insufficient data returns a less specialized
    /// context; the evaluator decides whether to request more data after considering all rules.
    /// Parsers do not throw <see cref="AdditionalDataNeededException"/> for incomplete input.
    ///
    /// Input storage is borrowed only for the duration of the call. Implementations may
    /// initialize capture metadata caches, but must not mutate packets, retain borrowed
    /// storage, or retain connection/parser state between calls. Returned contexts may reuse
    /// immutable data and shared capture references. Captures outlive the borrowed history span.
    ///
    /// The caller retains watched-host and selected-service routing information for acquisition
    /// handling. This contract does not put routing, buffering, or wake actions into predicates.
    /// ForwardAuth constructs its context directly without fabricating capture input.
    /// </remarks>
    public interface IFilterContextParser
    {
        /// <summary>
        /// Creates a context from one or more captures flowing in the same direction.
        /// A single capture describes the current packet; multiple captures provide the
        /// available data for one connection attempt, reconstructed by TCP sequence position.
        /// </summary>
        /// 
        /// <param name="captures">
        /// Nonempty, caller-owned captures in observation order. All captures must have the
        /// same source and target IP addresses; transport captures must also share the same
        /// source and target ports and protocol. Provisional address resolution may precede
        /// transport traffic. Demand histories contain only inbound traffic; outbound traffic
        /// is evaluated separately for accounting. The default parser uses only the latest
        /// UDP datagram, including an empty one, and never concatenates independent datagrams.
        /// A plugin may provide application-protocol-aware interpretation instead.
        /// Pass a single capture as [capture], without allocating a temporary array.
        /// The caller must not change the input during parsing.
        /// </param>
        /// 
        /// <param name="direction">
        /// Direction of every supplied capture relative to the watched host. This identifies
        /// the watched endpoint; it does not select a direction from a mixed history.
        /// </param>
        /// 
        /// <exception cref="ArgumentException">
        /// The input is empty or mixes directions, addresses or transport connection endpoints.
        /// </exception>
        /// <exception cref="MalformedServicePayloadException">
        /// Available data establishes an invalid service payload, rather than incomplete input.
        /// </exception>
        /// <exception cref="NotSupportedException">The capture has no usable IP address.</exception>
        IFilterContext Parse(ReadOnlySpan<INetworkCapture> captures, FlowDirection direction);
    }
}
