using System.Net;

namespace MadWizard.Desomnia.Network.Filter
{
    /// <summary>
    /// Requests IP unicast traffic after address resolution. The address identifies the
    /// watched destination to advertise, not the peer address in the filter context.
    /// </summary>
    public class IPUnicastNeededException(IPAddress ip) : AdditionalDataNeededException($"IP unicast traffic to {ip} is required for filtering.")
    {
        public IPAddress Address { get; } = ip ?? throw new ArgumentNullException(nameof(ip));
    }
}
