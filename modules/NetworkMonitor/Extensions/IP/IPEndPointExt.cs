namespace System.Net.NetworkInformation
{
    public static class IPEndPointExt
    {
        public static IPService? ToIPPort(this IPEndPoint endpoint)
        {
            switch (endpoint)
            {
                case TCPEndPoint:
                    return new IPService(IPProtocol.TCP, (ushort)endpoint.Port);

                case UDPEndPoint:
                    return new IPService(IPProtocol.UDP, (ushort)endpoint.Port);

                default:
                    return null;
            }
        }
    }
}
