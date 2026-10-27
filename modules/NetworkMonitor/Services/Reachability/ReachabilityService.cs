using ConcurrentCollections;
using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Device;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Watch;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Reachability
{
    public class ReachabilityService : INetworkService
    {
        public required ILogger<ReachabilityService> Logger { private get; init; }

        public required NetworkMonitor  Monitor { private get; init; }
        public required NetworkSegment  Network { private get; init; }
        public required NetworkDevice   Device  { private get; init; }

        public required ReachabilityCache Cache { private get; init; }

        readonly ConcurrentHashSet<ReachabilityTest> _pendingTests = [];

        void INetworkService.ProcessCapture(INetworkCapture capture)
        {
            if (capture.HasPacket<IPPacket>() && capture.SourceEndPoint is { } source && Network[source.Address] is NetworkHost host)
            {
                Notify(host, capture.SourcePhysicalAddress, source);
            }
        }

        public async Task<bool> Test(RemoteHostWatch watch, IPAddress? address = null, bool useCache = true, string label = "host")
        {
            var test = address != null ? new ReachabilityTest([address], watch.PingOptions.Timeout) : new HostReachabilityTest(watch);

            if (useCache)
            {
                bool? cached = null;
                if (Cache.Read(test).Values.Any(r => r == true)) // has any Options been reachable recently?
                    cached = true;
                else if (Cache.Read(test).Values.All(r => r == false)) // have all IPs been unreachable recently?
                    cached = false;

                if (cached.HasValue)
                {
                    Logger.LogTrace($"Testing reachability of {label} '{watch.Host.Name}': {(cached.Value ? "yes" : "no")} [cached]");

                    return cached.Value;
                }
            }

            Logger.LogTrace($"Testing reachability of {label} '{watch.Host.Name}':");

            try
            {
                TimeSpan latency = await Send(test);

                Logger.LogTrace($"Received response from '{watch.Host.Name}' after {Math.Ceiling(latency.TotalMilliseconds)} ms");

                return true;
            }
            catch (HostTimeoutException ex)
            {
                Logger.LogTrace($"Received NO response from '{watch.Host.Name}' after {Math.Ceiling(ex.Timeout.TotalMilliseconds)} ms");

                return false; // host is most probably offline
            }
        }

        public async Task<TimeSpan> MaybePingUntil(RemoteHostWatch watch, TimeSpan? timeout = null)
        {
            var ips = watch.Host.IPAddresses.Where(ip => watch.WakeOptions.Ping || !Network.LocalRange.Contains(ip));

            return await PingUntil(watch, ips, timeout);
        }

        public async Task<TimeSpan> PingUntil(RemoteHostWatch watch, IEnumerable<IPAddress> ips, TimeSpan? timeout = null)
        {
            using PeriodicTimer timer = new(watch.PingOptions.Timeout);

            async void PeriodicSendPing()
            {
                do foreach (var ip in ips) SendPing(ip); while (await timer.WaitForNextTickAsync());
            }

            PeriodicSendPing();

            return await Until(new HostReachabilityTest(watch, timeout));
        }

        /// <summary>
        /// Tests the reachability of a given host, passively. No requests will be sent to the network.
        /// </summary>
        /// 
        /// <param name="host">Host that should be checked, via all known Options addresses</param>
        /// <param name="timeout">TimeSpan we should wait for a response</param>
        /// 
        /// <returns>Time when we received the first response.</returns>
        /// <exception cref="HostTimeoutException">No response was received in the given TimeSpan</exception>
        public async Task<TimeSpan> Until(ReachabilityTest test)
        {
            return await MeasureLatency(test);
        }

        /// <summary>
        /// Sends out requests, to test the reachability of a given host.
        /// </summary>
        /// 
        /// <param name="test">Describes which addresses should be checked and how log we may wait for a response.</param>
        /// <param name="useICMP">Should we use ICMPv4 or ICMPv6 instead of ARP/NDP?</param>
        /// 
        /// <returns>Time until we received the first response. Otherwise throws HostTimeoutException</returns>
        /// <exception cref="NotImplementedException">Unknown address family given</exception>
        /// <exception cref="HostTimeoutException">No response was received in the given TimeSpan</exception>
        public async Task<TimeSpan> Send(ReachabilityTest test, bool useICMP = false)
        {
            _pendingTests.Add(test);

            foreach (var ip in test)
            {
                SendPing(ip, useICMP);
            }

            return await MeasureLatency(test);
        }

        private void SendPing(IPAddress ip, bool useICMP = false)
        {
            if (useICMP || !Network.LocalRange.Contains(ip))
                SendICMPEchoRequest(ip);
            else if (ip.AddressFamily == AddressFamily.InterNetwork)
                SendARPRequest(ip);
            else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                SendNDPNeighborSolicitation(ip);
            else
                throw new NotImplementedException($"Unsupported address family {ip.AddressFamily}");
        }

        private async Task<TimeSpan> MeasureLatency(ReachabilityTest test)
        {
            _pendingTests.Add(test);

            try
            {
                if (await test.RespondedTimely())
                {
                    return test.Elapsed;
                }

                throw new HostTimeoutException(test.Elapsed);
            }
            finally
            {
                FinishTest(test, test.Elapsed);
            }
        }

        private async void FinishTest(ReachabilityTest test, TimeSpan elapsed)
        {
            var untilTimeout = test.Timeout - elapsed;

            if (untilTimeout > TimeSpan.Zero)
            {
                await Task.Delay(untilTimeout);
            }

            if (_pendingTests.TryRemove(test))
            {
                foreach (var ip in test) // collect remaining results
                {
                    var endpoint = test is ServiceReachabilityTest service ? service.EndPoint : new IPEndPoint(ip, 0);

                    switch (test[ip])
                    {
                        case TimeSpan latency:
                            if (latency > elapsed) // already written to cache?
                                Cache.Write(endpoint, true);
                            break;

                        default:
                            Cache.Write(endpoint, false);
                            break;
                    }
                }

                test.Dispose();
            }
        }

        /// <summary>
        /// Records activity from a source endpoint. Port zero denotes address-only activity;
        /// a nonzero port requires an IPServiceEndPoint identifying the transport protocol.
        /// </summary>
        public void Notify(NetworkHost host, PhysicalAddress mac, IPEndPoint endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            if (endpoint.Port != 0 && endpoint is not IPServiceEndPoint)
                throw new ArgumentException("A nonzero source port requires an IPServiceEndPoint identifying its protocol.", nameof(endpoint));

            /**
             * We try to filter out all IP activity, were we should see the 
             * actual MAC address of the host as sender, but didn't.
             */
            if (host.PhysicalAddress is not PhysicalAddress hmac || hmac.Equals(mac) || !Network.LocalRange.Contains(endpoint.Address))
            {
                if (Monitor[host] is RemoteHostWatch watch)
                {
                    watch.LastSeen = DateTime.Now;
                }

                Cache.Write(endpoint, true);

                foreach (var test in _pendingTests)
                {
                    test.NotifyReachable(endpoint);
                }
            }
        }

        #region Address Resolution Requests
        private void SendICMPEchoRequest(IPAddress ip)
        {
            using var ping = new Ping();

            Logger.LogTrace($"Sending ICMP echo request for {ip}");

            // IMPROVE craft ICMP packet manually and send it via NetworkDevice, if necessary
            ping.SendPingAsync(ip, TimeSpan.FromMilliseconds(100), options: new(64, true));
        }

        internal void SendARPRequest(IPAddress ip, PhysicalAddress? mac = null)
        {
            if (ip.AddressFamily != AddressFamily.InterNetwork)
                throw new ArgumentException($"Only IPv4 is supported; got '{ip}'");
            if (Device.IPv4Address == null)
                throw new ArgumentException($"Device '{Device.Interface.Name}' does not have a IPv4 address.");

            Logger.LogTrace($"Sending ARP request for {ip}");

            var request = new EthernetPacket(Device.PhysicalAddress, mac ?? PhysicalAddressExt.Broadcast, EthernetType.Arp)
            {
                PayloadPacket = new ArpPacket(ArpOperation.Request,
                PhysicalAddressExt.Empty, ip, // target
                Device.PhysicalAddress, Device.IPv4Address) // source
            };

            Device.SendPacket(request);
        }

        internal void SendNDPNeighborSolicitation(IPAddress ip, PhysicalAddress? mac = null)
        {
            if (ip.AddressFamily != AddressFamily.InterNetworkV6)
                throw new ArgumentException($"Only IPv6 is supported; got '{ip}'");
            if (Device.IPv6LinkLocalAddress == null)
                throw new ArgumentException($"Device '{Device.Interface.Name}' does not have a link-local IPv6 address.");

            Logger.LogTrace($"Sending NDP neighbor solicitation for {ip}");

            var ipSource = Device.IPv6LinkLocalAddress;
            var ipTarget = ip.DeriveIPv6SolicitedNodeMulticastAddress();

            var request = new EthernetPacket(Device.PhysicalAddress, mac ?? ipTarget.DeriveLayer2MulticastAddress(), EthernetType.IPv6)
            {
                PayloadPacket = new IPv6Packet(ipSource, ipTarget).WithNDPNeighborSolicitation(ip, Device.PhysicalAddress)
            };

            Device.SendPacket(request);
        }
        #endregion
    }
}
