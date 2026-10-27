namespace System.Net
{
    public class IPServiceEndPoint(IPAddress ip, IPProtocol protocol, ushort port) : IPEndPoint(ip, port)
    {
        public IPProtocol Protocol { get; } = protocol is IPProtocol.TCP or IPProtocol.UDP
            ? protocol : throw new ArgumentOutOfRangeException(nameof(protocol), protocol, "An endpoint requires a single TCP or UDP protocol.");

        public IPService Service => new(Protocol, (ushort)Port);

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

        public static implicit operator IPService(IPServiceEndPoint endpoint) => endpoint.Service;
    }
}
