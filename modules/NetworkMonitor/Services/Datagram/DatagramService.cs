using MadWizard.Desomnia.Network.Neighborhood;
using Microsoft.Extensions.Logging;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Datagram
{
    /// <summary>
    /// An <see cref="INetworkService"/> processing the UDP datagrams of one port. They are sniffed
    /// off the wire via packet capturing; a service registered with <see cref="SocketMetadata"/>
    /// additionally receives them through an OS socket managed by <see cref="UDPSocketService"/> --
    /// kernel-reassembled, so IP-fragmented datagrams (which the capture path must drop) arrive
    /// whole. Both inlets converge on <see cref="ProcessDatagram"/>.
    /// </summary>
    public abstract class DatagramService(ushort port, string? realm = null) : INetworkService
    {
        public required ILogger<DatagramService> WireLogger { protected get; init; }

        public required NetworkDevice   Device  { internal get; init; }
        public required NetworkSegment  Network { protected get; init; }

        protected ushort  Port  => port;
        protected string? Realm => realm;

        public virtual async Task Startup() { }

        public virtual Task Resume() => Task.CompletedTask; // here to be overridden by subclasses

        void INetworkService.ProcessPacket(EthernetPacket packet)
        {
            using var scope = WireLogger.BeginRealmScope(realm);

            if (Device.HasSentPacket(packet) || packet.IsIPFragment())
                return; // cannot be interpreted here; but a linked OS socket can receive them reassembled

            if (packet.Extract<UdpPacket>() is UdpPacket udp && udp.DestinationPort == port && udp.PayloadData.Length > 0)
            {
                ProcessDatagram(new DatagramPacket(packet, udp));
            }
        }

        /// <summary>
        /// The socket inlet, invoked by <see cref="UDPSocketService"/> with kernel-reassembled
        /// datagrams: processing is serialized with the packet capture loop.
        /// </summary>
        internal async Task DeliverDatagram(DatagramPacket datagram)
        {
            using var scope = WireLogger.BeginRealmScope(realm);

            using (await Network.Mutex.LockAsync())
            {
                ProcessDatagram(datagram);
            }
        }

        /// <summary>A datagram addressed to this service's port, from either inlet.</summary>
        protected abstract void ProcessDatagram(DatagramPacket datagram);

        public virtual async Task Shutdown(NetworkShutdownReason reason) { }

        /// <summary>
        /// Registration metadata declaring that an OS socket should be allocated alongside the
        /// packet capturing: the <c>DefaultDatagramSocket</c> middleware links a service carrying
        /// it to the <see cref="UDPSocketService"/> at construction, and unlinks it -- closing the
        /// socket with its last user -- when the service's scope is disposed.
        /// </summary>
        public class SocketMetadata
        {
            /// <summary>The port to allocate; reserve ephemeral ports up-front via <see cref="UDPSocketService.Reserve"/>.</summary>
            public ushort Port   { get; set; }
            /// <summary>Whether the port may coexist with other reusable binders on the OS.</summary>
            public bool   Shared { get; set; }
        }
    }
}
