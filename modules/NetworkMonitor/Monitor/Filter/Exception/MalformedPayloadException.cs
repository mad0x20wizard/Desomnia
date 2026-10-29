using System.Net;

namespace MadWizard.Desomnia.Network.Filter
{
    /// <summary>
    /// The parser established invalid service payload. This is terminal for a DemandRequest
    /// and must never be handled as a retryable <see cref="AdditionalDataNeededException"/>.
    /// </summary>
    public class MalformedPayloadException(IPService service, string? message = null, Exception? innerException = null)
        : Exception(message ?? $"Invalid payload for service {service}.", innerException)
    {
        public IPService Service { get; } = service;
    }
}
