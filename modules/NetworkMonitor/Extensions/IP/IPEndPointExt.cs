namespace System.Net.NetworkInformation
{
    public static class IPEndPointExt
    {
        public static IPPort? ToIPPort(this IPEndPoint endpoint)
        {
            switch (endpoint)
            {
                case TCPEndPoint:
                    return new IPPort(IPProtocol.TCP, (ushort)endpoint.Port);

                case UDPEndPoint:
                    return new IPPort(IPProtocol.UDP, (ushort)endpoint.Port);

                default:
                    return null;
            }
        }
    }
}
