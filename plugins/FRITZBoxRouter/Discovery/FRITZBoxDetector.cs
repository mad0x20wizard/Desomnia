using Autofac;
using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Discovery;
using MadWizard.Desomnia.Network.FRITZ.API;
using MadWizard.Desomnia.Network.FRITZ.API.Model;
using MadWizard.Desomnia.Network.FRITZ.Configuration;
using MadWizard.Desomnia.Network.FRITZ.Context;
using MadWizard.Desomnia.Network.FRITZ.Neighborhood;
using MadWizard.Desomnia.Network.Naming;
using Makaretu.Dns;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.FRITZ.Discovery
{
    /// <summary>
    /// Turns every &lt;FRITZBoxRouter&gt; of this network into a full <see cref="FRITZBoxRouter"/>
    /// router via <see cref="NetworkContext.CreateRouter{T}"/>. For each box it first opens a
    /// client and enumerates the box' host table — that call doubles as the reachability and
    /// credentials check: if the box cannot be reached, no router is created for it. The VPN peers
    /// found there (layer-3 hosts with a fixed tunnel IP and no MAC) auto-populate the router's VPN
    /// client list, so the router comes up knowing its peers without them being spelled out in the
    /// configuration — explicitly configured &lt;VPNClient&gt; elements and hosts the network
    /// already knows take precedence.
    ///
    /// <para>The box needs neither an IP nor an explicit name in the configuration: a
    /// &lt;FRITZBoxRouter&gt; defaults its name to <c>fritz.box</c> (which every box answers for),
    /// and the client reaches the box by that name; the router's own addresses are then resolved
    /// by the ordinary host discovery, like any other host.</para>
    ///
    /// <para>Configured boxes are static: <see cref="IRouterDiscovery.ConfigureRouters"/> creates
    /// them unconditionally — declaring a &lt;FRITZBoxRouter&gt; is intent enough. Only the active
    /// mDNS lookup is gated on <c>autoDetect="Router"</c>: <see cref="IRouterDiscovery.DiscoverRouters"/>
    /// browses DNS-SD for <c>_tr064._tcp</c> and creates a router for every FRITZ!Box found on the
    /// segment without any configuration at all (zero-conf). Repeaters and powerline adapters
    /// advertise the same service, so only instances under the <c>fritz.box</c> domain — the routers
    /// — are adopted. Discovered boxes are reached unauthenticated (host/MAC enumeration only).</para>
    ///
    /// <para>The mDNS pass runs before the built-in detectors (order &lt; 1), so when a discovered
    /// box <em>is</em> the default gateway, <c>DefaultGatewayDetector</c> finds it by IP and only
    /// adds the gateway addresses instead of creating a second, generic router.</para>
    /// </summary>
    internal class FRITZBoxDetector(IEnumerable<FRITZBoxRouterInfo> staticBoxes, DiscoveryOptions options) : IRouterDiscovery
    {
        /// <summary>The DNS-SD service every FRITZ!Box (and repeater/powerline) advertises itself under.</summary>
        private static readonly DomainName ServiceDomainName = new("_tr064", "_tcp", "local");
        /// <summary>The domain a FRITZ!Box <em>router</em> owns; repeaters use <c>fritz.repeater</c>,
        /// powerline adapters <c>fritz.powerline</c> — same service, different device.</summary>
        private static readonly DomainName RouterDomainName = new("fritz","box");

        public required ILogger<FRITZBoxDetector> Logger { private get; init; }

        public required MulticastServiceBrowser Browser { private get; init; }

        // Statically-configured boxes — always created (autoDetect="Router" not required).
        async Task IRouterDiscovery.ConfigureRouters(NetworkContext ctx)
        {
            foreach (var config in staticBoxes)
            {
                await CreateFRITZBoxRouter(ctx, config);
            }
        }

        // Active lookup — runs only under autoDetect="Router" (the IRouterDiscovery pipeline gate).
        async Task IRouterDiscovery.DiscoverRouters(NetworkContext ctx)
        {
            await DiscoverViaMDNS(ctx);
        }

        /// <summary>Creates a router for one box, unless it is already present. Enumerating the box'
        /// hosts is the reachability probe: if it throws, the box is unreachable and no router is
        /// created. The proven client is handed to the router scope, which owns it from then on.</summary>
        private async Task CreateFRITZBoxRouter(NetworkContext ctx, FRITZBoxRouterInfo config)
        {
            if (ctx.Network[config.Name] is not null)
                return; // already present
            if (config.IPv4 is IPAddress v4 && ctx.Network[v4] is not null)
                return;
            if (config.IPv6 is IPAddress v6 && ctx.Network[v6] is not null)
                return;

            var client = CreateClient(config);

            try
            {
                await PopulateVPNClients(client, config, ctx);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "FRITZ!Box '{Box}' could not be reached — not creating a router for it.", config.Name);
                client.Dispose();
                return;
            }

            var router = await ctx.CreateRouter<FRITZBoxRouterContext>(config, TypedParameter.From(client));
        }

        /// <summary>Browses <c>_tr064._tcp</c> and creates a router for every FRITZ!Box on the segment
        /// (fritz.box domain) that isn't configured or already known.</summary>
        private async Task DiscoverViaMDNS(NetworkContext ctx)
        {
            // Collect matching instances during the browse window; keeping the references alive lets
            // the browser enrich them with their addresses. Read them (and create) after the window.
            var found = new Dictionary<string, ServiceInstance>(StringComparer.OrdinalIgnoreCase);

            using var cts = new CancellationTokenSource(options.Timeout);
            try
            {
                using var request = Browser.EnumerateInstances(ServiceDomainName, cts.Token);

                await foreach (var instance in request)
                {
                    if (instance.HostDomainName.Equals(RouterDomainName)) // only accept Mesh Master
                    {
                        found[instance.Name] = instance; // dedup by friendly name
                    }
                }
            }
            catch (OperationCanceledException) { /* browse window elapsed */ }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "mDNS browse for FRITZ!Box routers failed.");
                return;
            }

            foreach (var instance in found.Values)
            {
                var config = BuildConfig(instance);

                Logger.LogDebug("Discovered FRITZ!Box router '{Name}' via mDNS.", config.Name);

                await CreateFRITZBoxRouter(ctx, config);
            }
        }

        /// <summary>Builds a zero-conf (credential-less) config from an mDNS instance: the friendly
        /// name as identity, the SRV target as the resolvable hostname, and the box' own advertised
        /// address (TXT <c>ipv4=/ipv6=</c>, else a resolved A/AAAA) so the client reaches it directly.</summary>
        private static FRITZBoxRouterInfo BuildConfig(ServiceInstance instance)
        {
            var (txtV4, txtV6) = ExtractTXTAddresses(instance.Properties);

            return new FRITZBoxRouterInfo
            {
                Name = instance.Name,
                HostName = instance.HostDomainName.ToString(),

                IPv4 = txtV4 ?? instance.Addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork),
                IPv6 = txtV6 ?? instance.Addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6),
            };
        }

        /// <summary>The FRITZ!Box <c>_tr064._tcp</c> TXT record carries the box' addresses as
        /// <c>ipv4=…</c>/<c>ipv6=…</c> entries; this is the authoritative, box-specific address.</summary>
        private static (IPAddress? v4, IPAddress? v6) ExtractTXTAddresses(IDictionary<string, string> properties)
        {
            IPAddress? ipv4 = null, ipv6 = null;
            if (properties.TryGetValue("ipv4", out string? s4))
                _ = IPAddress.TryParse(s4, out ipv4);
            if (properties.TryGetValue("ipv6", out string? s6))
                _ = IPAddress.TryParse(s6, out ipv6);

            return (ipv4, ipv6);
        }

        private FRITZBoxClient CreateClient(FRITZBoxRouterInfo config)
        {
            // Prefer a configured address; otherwise reach the box by name (fritz.box by default),
            // which it resolves for itself. IPv6 literals must be bracketed for the URI authority.
            string host = config.IPv4?.ToString()
                ?? (config.IPv6 is { } v6 ? $"[{v6}]" : null)
                ?? config.HostName
                ?? config.Name;

            return new FRITZBoxClient(host, config.Credentials, config.TLS, Logger);
        }

        private async Task PopulateVPNClients(FRITZBoxClient client, FRITZBoxRouterInfo config, NetworkContext ctx)
        {
            var hosts = await client.GetHostsAsync(default);

            // Authenticated: the box flags VPN peers directly. Anonymous: it doesn't, so — only when
            // the user asked for VPN autodetect — infer a peer from the tell-tale shape of a tunnel
            // host: an IP but no MAC (in the anonymous host table every leased LAN host has a MAC).
            IEnumerable<FritzHost> peers = hosts.Where(h => h.IsVPN);

            AutoDiscoveryType auto = config.AutoDetect ?? ctx.Config.AutoDetect;
            if (!client.CanAuthenticate && auto.HasFlag(AutoDiscoveryType.VPN))
                peers = hosts.Where(h => h.MAC is null && h.IP is not null);

            foreach (var peer in peers)
            {
                if (string.IsNullOrWhiteSpace(peer.Name))
                    continue;
                if (config.VPNClient.Any(c => string.Equals(c.Name, peer.Name, StringComparison.OrdinalIgnoreCase)))
                    continue; // explicitly configured
                if (ctx.Network[peer.Name] is not null || (peer.IP is not null && ctx.Network[peer.IP] is not null))
                    continue; // already known to the network

                var info = new NetworkHostInfo
                {
                    Name = peer.Name,

                    AutoDetect = AutoDiscoveryType.Nothing, // the tunnel IP is authoritative
                };

                switch (peer.IP?.AddressFamily)
                {
                    case AddressFamily.InterNetwork:
                        info.IPv4 = peer.IP;
                        break;
                    case AddressFamily.InterNetworkV6:
                        info.IPv6 = peer.IP;
                        break;
                }

                config.VPNClient.Add(info);

                Logger.LogDebug("Discovered VPN client '{Name}' on FRITZ!Box '{Box}'.", peer.Name, config.Name);
            }
        }
    }
}
