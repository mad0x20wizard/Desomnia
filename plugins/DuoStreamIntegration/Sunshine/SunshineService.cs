using MadWizard.Desomnia.Network.Neighborhood.Services;
using System.Net;

namespace MadWizard.Desomnia.Service.Duo.Sunshine
{
    public class SunshineService(ushort port) : TransportNetworkService($"Sunshine:{port}", new(IPProtocol.TCP, port))
    {
        // TCP
        internal IPPort HTTP      => new(IPProtocol.TCP, (ushort)(port + 0));
        internal IPPort HTTPS     => new(IPProtocol.TCP, (ushort)(port - 5));
        internal IPPort Web       => new(IPProtocol.TCP, (ushort)(port + 1));
        internal IPPort RTSP      => new(IPProtocol.TCP, (ushort)(port + 21));

        // UDP
        internal IPPort Video     => new(IPProtocol.UDP, (ushort)(port + 9));
        internal IPPort Control   => new(IPProtocol.UDP, (ushort)(port + 10));
        internal IPPort Audio     => new(IPProtocol.UDP, (ushort)(port + 11));
        internal IPPort Mic       => new(IPProtocol.UDP, (ushort)(port + 13));

        public bool IsWaitingForClient => PortChecker.IsAnyPortInUse(HTTP);
        public bool HasClientConnected => PortChecker.IsAnyPortInUse(Video, Control, Audio, Mic);

        protected override IEnumerable<IPPort> Ports
        {
            get
            {
                yield return HTTP;
                yield return HTTPS;
                yield return Web;
                yield return RTSP;
                yield return Video;
                yield return Control;
                yield return Audio;
                yield return Mic;
            }
        }
    }
}