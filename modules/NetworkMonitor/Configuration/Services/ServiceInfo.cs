using MadWizard.Desomnia.Network.Neighborhood;
using System.Net;

namespace MadWizard.Desomnia.Network.Configuration.Services
{
    /// <summary>
    /// https://www.iana.org/assignments/service-names-port-numbers/service-names-port-numbers.xml
    /// </summary>
    public class ServiceInfo()
    {
        public required string  Name                { get; set; }
        public string?          ServiceName         { get; set; }

        /// <summary>
        /// Optional DNS-SD instance label the service is advertised under when it differs from the
        /// host's name (see <see cref="NetworkService.InstanceName"/>).
        /// </summary>
        public string?          InstanceName        { get; set; }

        /// <summary>DNS-SD SRV priority / weight advertised for the service (RFC 2782).</summary>
        public ushort           Priority            { get; set; }
        public ushort           Weight              { get; set; }

        /// <summary>DNS-SD TXT attributes advertised for the service (RFC 6763 §6).</summary>
        public IDictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();

        public IPProtocol       Protocol            { get; set; } = IPProtocol.TCP;
        public ushort           Port                { get; set; }

        public static implicit operator IPService(ServiceInfo info)
        {
            if (!info.Protocol.IsSingle)
                throw new FormatException($"Unsupported service protocol: {info.Protocol}.");

            return new(info.Protocol, info.Port);
        }

        public virtual NetworkService Service => new(Name, this)
        {
            ServiceName = ServiceName!
        };
    }
}
