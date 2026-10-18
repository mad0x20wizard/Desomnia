using MadWizard.Desomnia.Network.Device;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Datagram
{
    /// <summary>
    /// The application-wide keeper -- and sole owner -- of OS-level UDP sockets: allocates (and
    /// thereby actually reserves) a port once (ephemeral or explicit, exclusive or shared with
    /// other programs on the OS), links any number of <see cref="DatagramService"/>s to it for
    /// datagram delivery, and closes the socket again with its last link. The sockets themselves
    /// are an implementation detail of this class: datagrams surface as
    /// <see cref="DatagramPacket"/>s, and replies travel back through
    /// <see cref="DatagramPacket.TryRespond"/>. Linking is declarative: a service registered with
    /// <see cref="DatagramService.SocketMetadata"/> is linked at construction (and unlinked at
    /// disposal) by the <c>DefaultDatagramSocket</c> middleware.
    /// </summary>
    public class UDPSocketService(ILoggerFactory loggers) : IDisposable
    {
        private readonly object _lock = new();

        private readonly Dictionary<ushort, Entry> _sockets = [];

        /// <summary>
        /// Ensures a socket for the port exists and returns its (possibly OS-assigned) port number:
        /// an explicit <paramref name="port"/> joins a matching earlier allocation; <c>null</c>
        /// allocates an exclusive socket on an ephemeral port.
        /// </summary>
        /// <exception cref="InvalidOperationException">The port is already allocated with the opposite sharing characteristic.</exception>
        /// <exception cref="SocketException">The OS refused the bind (port taken elsewhere).</exception>
        public ushort Reserve(ushort? port = null, bool shared = false)
        {
            lock (_lock)
            {
                return ReserveEntry(port, shared).Socket.Port;
            }
        }

        /// <summary>
        /// Couples datagram delivery: datagrams received on the port (and addressed to the
        /// service's device) are handed to the service. Dispose the handle to decouple --
        /// the socket closes with its last link.
        /// </summary>
        public IDisposable Link(DatagramService service, ushort port, bool shared = false)
        {
            lock (_lock)
            {
                var entry = ReserveEntry(port, shared);

                entry.Links++;

                var socket = entry.Socket;

                var subscription = socket.Listen(service.Device, (source, target, payload) =>
                {
                    return service.DeliverDatagram(new DatagramPacket(response => socket.Send(response, source), source, target, payload));
                });

                return new LinkHandle(() => Unlink(entry, subscription));
            }
        }

        private Entry ReserveEntry(ushort? port, bool shared)
        {
            if (port is ushort existing && _sockets.TryGetValue(existing, out var entry))
            {
                if (entry.Shared != shared)
                    throw new InvalidOperationException($"UDP port {existing} is already allocated {(entry.Shared ? "shared" : "exclusively")}.");

                return entry;
            }

            UDPSocket socket = shared && port is ushort preferred
                ? new SharedUDPSocket(preferred, loggers.CreateLogger<UDPSocketService>())
                : new UDPSocket(port ?? 0, loggers.CreateLogger<UDPSocketService>());

            return _sockets[socket.Port] = new Entry(socket, shared);
        }

        private void Unlink(Entry entry, IDisposable subscription)
        {
            lock (_lock)
            {
                subscription.Dispose();

                if (--entry.Links <= 0)
                {
                    entry.Socket.Dispose();

                    _sockets.Remove(entry.Socket.Port);
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var entry in _sockets.Values)
                    entry.Socket.Dispose();

                _sockets.Clear();
            }
        }

        private sealed class Entry(UDPSocket socket, bool shared)
        {
            public UDPSocket Socket => socket;
            public bool      Shared => shared;

            public int Links { get; set; }
        }

        private sealed class LinkHandle(Action unlink) : IDisposable
        {
            private Action? _unlink = unlink;

            public void Dispose() => Interlocked.Exchange(ref _unlink, null)?.Invoke();
        }

        /// <summary>
        /// An OS-level UDP socket, bound in dual mode (IPv4 + IPv6, all interfaces). Datagrams
        /// arrive kernel-reassembled (IP-fragmented requests up to the 65,535-byte UDP maximum
        /// come in whole, which a packet capture can never deliver) and are routed to the listener
        /// of the network device they were addressed to. Holding the port also stops the kernel
        /// from answering requests with ICMP port-unreachable.
        /// </summary>
        private class UDPSocket : IDisposable
        {
            private readonly ILogger _logger;

            private readonly object _lock = new();

            private readonly List<Listener> _listeners = [];

            private readonly Socket _socket;

            /// <summary>The port the socket is bound to (assigned by the OS when created for port 0).</summary>
            public ushort Port { get; }

            /// <exception cref="SocketException">The requested port is already taken.</exception>
            public UDPSocket(ushort port, ILogger logger)
            {
                _logger = logger;

                _socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp) { DualMode = true };

                DisableICMPReset(_socket);

                Configure(_socket);

                _socket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));

                Port = (ushort)((IPEndPoint)_socket.LocalEndPoint!).Port;

                _logger.LogDebug("UDP socket bound to port {Port}", Port);

                _ = ReceiveLoop(_socket);
            }

            /// <summary>Adjusts the socket's options before it is bound.</summary>
            protected virtual void Configure(Socket socket) { }

            /// <summary>
            /// Subscribes <paramref name="receive"/> for datagrams addressed to
            /// <paramref name="device"/>; dispose the returned handle to unsubscribe. A lone
            /// listener receives every datagram, so a single-interface setup needs no address match.
            /// </summary>
            public IDisposable Listen(NetworkDevice device, Func<IPEndPoint, IPEndPoint, byte[], Task> receive)
            {
                lock (_lock)
                {
                    var listener = new Listener(this, device, receive);

                    _listeners.Add(listener);

                    return listener;
                }
            }

            /// <summary>Sends <paramref name="payload"/> to <paramref name="target"/> -- the reply path for datagrams received here.</summary>
            public void Send(byte[] payload, IPEndPoint target)
            {
                // a dual-mode socket expects IPv6(-mapped) addresses
                if (target.Address.AddressFamily == AddressFamily.InterNetwork)
                    target = new IPEndPoint(target.Address.MapToIPv6(), target.Port);

                _socket.SendTo(payload, target);
            }

            private async Task ReceiveLoop(Socket socket)
            {
                var buffer = new byte[ushort.MaxValue];
                var anyEndpoint = new IPEndPoint(IPAddress.IPv6Any, 0);

                while (true)
                {
                    SocketReceiveMessageFromResult result;

                    try
                    {
                        result = await socket.ReceiveMessageFromAsync(buffer, SocketFlags.None, anyEndpoint);
                    }
                    catch (ObjectDisposedException)
                    {
                        return; // socket disposed: the loop's regular end
                    }
                    catch (SocketException ex)
                    {
                        _logger.LogTrace("UDP receive failed: {Error}", ex.SocketErrorCode);

                        continue;
                    }

                    // dual-mode reception surfaces IPv4 peers as v4-mapped IPv6 addresses
                    var source = Unmap((IPEndPoint)result.RemoteEndPoint);
                    var target = new IPEndPoint(Unmap(result.PacketInformation.Address), Port);

                    var payload = buffer[..result.ReceivedBytes].ToArray();

                    try
                    {
                        await Dispatch(source, target, payload);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process a datagram from {Source}", source);
                    }
                }
            }

            private async Task Dispatch(IPEndPoint source, IPEndPoint target, byte[] payload)
            {
                Listener[] listeners;
                lock (_lock)
                {
                    listeners = [.. _listeners];
                }

                // route by the address the datagram was sent to; a lone listener takes anything
                var claimed = listeners.Where(listener => Owns(listener.Device, target.Address)).ToArray();

                if (claimed.Length == 0 && listeners.Length == 1)
                    claimed = listeners;

                foreach (var listener in claimed)
                    await listener.Receive(source, target, payload);
            }

            private static bool Owns(NetworkDevice device, IPAddress address)
            {
                return (device.IPv4Address          is IPAddress v4 && address.Equals(v4))
                    || (device.IPv6LinkLocalAddress is IPAddress v6 && address.Equals(v6));
            }

            private static IPAddress  Unmap(IPAddress address)   => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
            private static IPEndPoint Unmap(IPEndPoint endpoint) => new(Unmap(endpoint.Address), endpoint.Port);

            /// <summary>
            /// On Windows, an ICMP port-unreachable from any peer would otherwise surface as a
            /// <see cref="SocketException"/> on subsequent receives, disturbing the shared loop.
            /// </summary>
            private static void DisableICMPReset(Socket socket)
            {
                if (!OperatingSystem.IsWindows())
                    return;

                const int SIO_UDP_CONNRESET = unchecked((int)0x9800000C);

                try
                {
                    socket.IOControl(SIO_UDP_CONNRESET, [0, 0, 0, 0], null);
                }
                catch (SocketException)
                {
                    // merely a comfort option
                }
            }

            public void Dispose()
            {
                lock (_lock)
                {
                    _socket.Dispose();

                    _listeners.Clear();
                }
            }

            private sealed class Listener(UDPSocket socket, NetworkDevice device, Func<IPEndPoint, IPEndPoint, byte[], Task> receive) : IDisposable
            {
                public NetworkDevice Device => device;

                public Func<IPEndPoint, IPEndPoint, byte[], Task> Receive => receive;

                public void Dispose()
                {
                    lock (socket._lock)
                    {
                        socket._listeners.Remove(this);
                    }
                }
            }
        }

        /// <summary>
        /// A <see cref="UDPSocket"/> whose port may also be held by other programs on the OS that
        /// bind it reusably (e.g. an OS mDNS responder). Which binder receives a given unicast
        /// datagram is then up to the OS -- suitable for ports whose traffic is also observed via
        /// packet capturing.
        /// </summary>
        private sealed class SharedUDPSocket(ushort port, ILogger logger) : UDPSocket(port, logger)
        {
            protected override void Configure(Socket socket)
            {
                // let other programs (e.g. an OS mDNS responder) hold the same port
                socket.ExclusiveAddressUse = false;
                socket.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress,
                    true);
            }
        }
    }
}
