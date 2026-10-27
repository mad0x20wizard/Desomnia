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
        /// Creates a context for passive evaluation of the current capture. Payload properties
        /// describe this packet only, even if it is part of a longer transport exchange.
        /// </summary>
        /// 
        /// <param name="capture">
        /// The shared lazy metadata cache for one logical packet, possibly assembled from
        /// several Ethernet frames. Both directional evaluations reuse its cached values.
        /// </param>
        /// 
        /// <param name="direction">Traffic direction relative to the watched host.</param>
        /// 
        /// <exception cref="MalformedServicePayloadException">
        /// Available data establishes an invalid service payload, rather than incomplete input.
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// The capture has no IP address, or needs IP fragmentation reassembly before parsing.
        /// </exception>
        IFilterContext Parse(INetworkCapture capture, FlowDirection direction);

        /// <summary>
        /// Creates a context from the retained history of one DemandRequest. Payload properties
        /// describe reconstructed data for its connection and the requested direction.
        /// Use this overload even when the demand currently contains only one capture.
        /// </summary>
        /// 
        /// <param name="captures">
        /// Nonempty, caller-owned history in observation order, including the newest capture.
        /// The history starts with the inbound demand trigger, establishing the watched and
        /// peer sides. Any transport captures must belong to the selected connection attempt.
        /// It may contain provisional address-resolution captures and both traffic directions.
        /// The parser selects the applicable data and reconstructs TCP by sequence position,
        /// not arrival order. The default parser uses only the latest UDP datagram in the
        /// requested direction; independent datagrams are never concatenated. A plugin may
        /// override this with application-protocol-aware interpretation.
        /// The caller must not change the history during parsing.
        /// </param>
        /// 
        /// <param name="direction">Traffic direction relative to the watched host.</param>
        /// 
        /// <exception cref="ArgumentException">
        /// The history is empty or mixes transport endpoints from different connection attempts.
        /// </exception>
        /// <exception cref="MalformedServicePayloadException">
        /// Available data establishes an invalid service payload, rather than incomplete input.
        /// </exception>
        IFilterContext Parse(ReadOnlySpan<INetworkCapture> captures, FlowDirection direction);
    }
}
