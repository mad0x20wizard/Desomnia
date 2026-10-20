using System.Net;

namespace MadWizard.Desomnia.Network.Filter
{
    /// <summary>
    /// Requests more payload for the watched transport service. This also covers an existing
    /// generic payload context that cannot yet satisfy a specialized requirement such as HTTP.
    /// </summary>
    public class ServicePayloadNeededException(IPService service, IPEndPoint remoteEndPoint)
        : AdditionalDataNeededException($"More payload from {remoteEndPoint} for service {service} is required for filtering.")
    {
        /// <summary>The watched-side protocol and port.</summary>
        public IPService Service { get; } = service;

        /// <summary>
        /// The peer's endpoint for this evaluation, retained for acquisition and response handling.
        /// Reuse the context's cached endpoint and treat it as immutable.
        /// </summary>
        public IPEndPoint RemoteEndPoint { get; } = remoteEndPoint ?? throw new ArgumentNullException(nameof(remoteEndPoint));
    }
}
