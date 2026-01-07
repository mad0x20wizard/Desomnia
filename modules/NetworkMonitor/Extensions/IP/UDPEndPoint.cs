namespace System.Net
{
    public class UDPEndPoint(IPAddress ip, ushort port) : TransportEndPoint(ip, IPProtocol.UDP, port)
    {

    }
}
