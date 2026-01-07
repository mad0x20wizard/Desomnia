namespace System.Net
{
    public abstract class TransportEndPoint(IPAddress ip, IPProtocol protocol, ushort port) : IPEndPoint(ip, port)
    {
        public IPProtocol Protocol => protocol;

        public override bool Equals(object? obj)
        {
            return obj is TransportEndPoint that && base.Equals(obj) && this.Protocol == that.Protocol;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(base.GetHashCode(), Protocol);
        }

        public override string ToString()
        {
            return base.ToString() + "/" + protocol.ToString().ToLower();
        }

        public static implicit operator IPPort(TransportEndPoint endpoint) => new(endpoint.Protocol, (ushort)endpoint.Port);
    }
}
