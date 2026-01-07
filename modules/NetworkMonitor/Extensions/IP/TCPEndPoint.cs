namespace System.Net
{
    public class TCPEndPoint(IPAddress ip, ushort port) : TransportEndPoint(ip, IPProtocol.TCP, port)
    {

    }
}
