namespace System.Net
{
    public class IPServiceEndPoint : IPEndPoint
    {
        public IPProtocol Protocol { get; }

        public IPServiceEndPoint(IPAddress ip, IPService service) : this(ip, service.Protocol, service.Port) { }

        public IPServiceEndPoint(IPAddress ip, IPProtocol protocol, ushort port) : base(ip, port)
        {
            if (protocol is not (IPProtocol.TCP or IPProtocol.UDP))
            {
                throw new ArgumentOutOfRangeException(nameof(protocol), protocol, "An endpoint requires a single TCP or UDP protocol.");
            }

            Protocol = protocol;
        }

        public override bool Equals(object? obj)
        {
            return obj is IPServiceEndPoint that && base.Equals(obj) && this.Protocol == that.Protocol;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(base.GetHashCode(), Protocol);
        }

        public override string ToString()
        {
            return base.ToString() + "/" + Protocol.ToString().ToLower();
        }

        public static implicit operator IPService(IPServiceEndPoint endpoint) => new(endpoint.Protocol, (ushort)endpoint.Port);
    }
}
