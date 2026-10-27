using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Filter;
using Makaretu.Dns;
using System.Net;

namespace MadWizard.Desomnia.Network.Neighborhood.Services
{
    public class TransportNetworkService(string name, IPService port) : NetworkService(name)
    {
        public virtual string ServiceName
        {
            get
            {
                field ??= Name.ToLower();

                return field;
            }

            set;
        }

        /// <summary>The DNS-SD service type of a service, e.g. "_ssh._tcp.local"; <c>null</c> if it has no service name set.</summary>
        public DomainName LocalDomainName => field ??= new DomainName($"_{ServiceName}", $"_{port.Protocol.ToString().ToLower()}", "local");

        /// <summary>
        /// Optional DNS-SD instance label this service is advertised under when it differs from the
        /// host's name -- e.g. taken over from a Sleep Proxy registration whose services carried
        /// their own instance names. <c>null</c> means the host's name is used.
        /// </summary>
        public string? InstanceName { get; set; }

        /// <summary>The SRV record priority advertised for this service (RFC 2782); lower is preferred.</summary>
        public ushort Priority { get; set; }
        /// <summary>The SRV record weight advertised for this service (RFC 2782), among equal priorities.</summary>
        public ushort Weight   { get; set; }

        /// <summary>
        /// The key/value pairs advertised in this service's DNS-SD TXT record (RFC 6763 §6) --
        /// e.g. carried over from a Sleep Proxy registration's TXT records. Empty means the service
        /// advertises a single empty TXT string.
        /// </summary>
        public IDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();

        public IPService Port => port;

        protected internal virtual IEnumerable<IPService> Ports
        {
            get
            {
                yield return port;
            }
        }

        /// <summary>Enumerates every port accepted by this service, including derived service ports.</summary>
        public IEnumerable<IPService> GetPorts() => Ports;

        public override bool Accepts(INetworkCapture capture, FlowDirection direction)
        {
            var endpoint = direction == FlowDirection.Inbound ? capture.TargetEndPoint : capture.SourceEndPoint;

            if (endpoint is IPServiceEndPoint transport)
            {
                return Ports.Any(service => service.Protocol.HasFlag(transport.Protocol) && service.Port == transport.Port);
            }

            return false;
        }

        public bool Serves(IPService another) => Ports.Any(service => service == another);

        public override string ToString()
        {
            return $"{port}{(Ports.Count() > 1 ? "(+)" : "")} (\"{Name}\")";
        }
    }

    public static class NetworkHostExt
    {
        extension (IEnumerable<NetworkService> services)
        {
            public TransportNetworkService? WithPort(IPService port)
            {
                return services.OfType<TransportNetworkService>().Where(t => t.Serves(port)).FirstOrDefault();
            }
        }
    }
}
