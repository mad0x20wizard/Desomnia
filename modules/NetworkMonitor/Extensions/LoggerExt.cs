using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Demand;
using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;
using System.Reactive.Disposables;

namespace Microsoft.Extensions.Logging
{
    public static class LoggerExt
    {
        public static IDisposable? BeginRequestScope(this ILogger logger, HostDemandWatch watch, DemandRequest request, TimeSpan evaluation)
        {
            var scope = new CompositeDisposable
            {
                Disposable.Create(() => // should be called first, so that it logs into the request scope
                {
                    logger.LogTrace($"END {request}; duration = {Math.Floor(request.Duration.TotalMilliseconds)} ms");
                })
            };

            if (logger.BeginHostScope(request.Host) is IDisposable dHost)
                scope.Add(dHost);

            object source = watch.DetermineSource(request.SourceAddress);
            string sourceName = (source is NetworkHost host ? host.Name : source.ToString())!;

            if (logger.BeginScope(new Dictionary<string, object> { ["Source"] = source! }) is IDisposable dSource)
                scope.Add(dSource);

            if (logger.BeginScope(new Dictionary<string, object> { ["Request"] = request }) is IDisposable dRequest)
                scope.Add(dRequest);

            logger.LogTrace($"BEGIN {request}: '{sourceName}' -> '{request.Host.Name}'; evaluate = {Math.Round(evaluation.TotalMilliseconds)} ms");

            return scope;
        }

        public static IDisposable? BeginRequestPacketScope(this ILogger logger, EthernetPacket packet)
        {
            var scope = logger.BeginScope(new Dictionary<string, object> { ["Packet"] = packet });

            logger.LogTrace($"VERIFY packet = \n{packet.ToTraceString()}");

            return scope;
        }

        public static IDisposable? BeginHostScope(this ILogger logger, NetworkHost networkHost)
        {
            if (!NLog.ScopeContext.TryGetProperty("Host", out _))
            {
                return logger.BeginScope(new Dictionary<string, object> { ["Host"] = networkHost });
            }

            return null;
        }

        public static async Task LogRequestError(this ILogger logger, DemandEvent @event, Exception ex)
        {
            await logger.LogEvent(LogLevel.Error, $"Error processing {@event};", @event.Host, @event.TriggerPacket, @event.Service, ex);
        }

        internal static async Task LogWakeTimeout(this ILogger logger, DemandEvent @event, TimeSpan timeout)
        {
            await logger.LogEvent(LogLevel.Warning, "Timeout at", @event.Host, @event.TriggerPacket, @event.Service, latency: timeout);
        }

        public static async Task LogWake(this ILogger logger, DemandEvent @event, TimeSpan? latency)
        {
            if (latency is TimeSpan duration)
                await logger.LogEvent(null, @event.ToMethod(), @event.Host, @event.TriggerPacket, @event.Service, latency: duration);
            else
                await logger.LogEvent(LogLevel.Warning, "Could not " + @event.ToMethod(), @event.Host, @event.TriggerPacket, @event.Service);
        }

        public static async Task LogKnock(this ILogger logger, DemandEvent @event, TimeSpan latency)
        {
            string log = "Knocked at \"" + @event.Host.Name + "\"";

            if (@event.Service is NetworkService service)
            {
                log += $" to access {service}";
            }

            log += $" [{Math.Floor(latency.TotalMilliseconds)} ms]";

            logger.LogInformation($"{log}");
        }

        internal static async Task LogKnockTimeout(this ILogger logger, DemandEvent @event, TimeSpan timeout)
        {
            string log = "Timeout while knocking at \"" + @event.Host.Name + "\"";

            if (@event.Service is NetworkService service)
            {
                log += $" to access {service}";
            }

            log += $" [{Math.Floor(timeout.TotalMilliseconds)} ms]";

            logger.LogWarning($"{log}");
        }

        public static async Task LogEvent(this ILogger logger, LogLevel? level, string method, NetworkHost host, EthernetPacket? trigger = null, NetworkService? service = null, Exception? ex = null, TimeSpan? latency = null)
        {
            string description = host.ToTarget();

            if (trigger != null)
            {
                description += $", triggered by {await host.Network.ToTrigger(trigger)}";
            }

            if (service != null)
            {
                description += $", using {service}";
            }

            if (latency != null)
            {
                description += $" [{Math.Floor(latency.Value.TotalMilliseconds)} ms]";
            }

            logger.Log(level ?? host.ToLevel(), ex, $"{method} {description}");
        }

        public static void LogHostPhysicalAddressChanged(this ILogger logger, NetworkHost host, PhysicalAddress mac)
        {
            using var scope = logger.BeginHostScope(host);

            logger.LogDebug("Host '{HostName}' is at {MAC}", host.Name, mac.ToHexString());
        }

        public static void LogHostAddressAdded(this ILogger logger, NetworkHost host, IPAddress ip)
        {
            using var scope = logger.BeginHostScope(host);

            logger.LogDebug("Add {Family} address '{IPAddress}' to host '{HostName}' "
                + (host.Network.LocalRange.Contains(ip) ? "[link-local]" : "[remote]"),

                ip.ToFamilyName(), ip,
                host.Name);
        }

        public static void LogHostAddressRemoved(this ILogger logger, NetworkHost host, IPAddress ip, bool hasExpired = false)
        {
            using var scope = logger.BeginHostScope(host);

            logger.LogDebug("Removed {Family} address '{IPAddress}' from host '{HostName}'"
                + (hasExpired ? " (expired)" : ""),

                ip.ToFamilyName(), ip,
                host.Name);
        }
    }

    file static class LogHelper
    {
        public static string ToMethod(this DemandEvent @event)
        {
            if (@event.TriggerPacket?.Extract<WakeOnLanPacket>() is not null)
                return "Rerouted";

            return "Send";
        }

        public static LogLevel ToLevel(this NetworkHost host)
        {
            return LogLevel.Information;
        }

        public static async Task<string> ToTrigger(this DemandEvent @event)
        {
            return await ToTrigger(@event.Host.Network, @event.TriggerPacket);
        }

        public static async Task<string> ToTrigger(this NetworkSegment network, EthernetPacket packet)
        {
            PhysicalAddress?    sourceMac   = packet.FindSourcePhysicalAddress();
            IPAddress?          sourceIP    = packet.FindSourceIPAddress();

            string trigger = sourceIP != null ? sourceIP.ToString() : sourceMac != null ? sourceMac.ToHexString() : "unknown";

            if (await network.LookupHostName(sourceMac, sourceIP) is string name)
            {
                trigger += $" (\"{name}\")";
            }

            return trigger;
        }


        public static string ToTarget(this NetworkHost host)
        {
            if (host is VirtualNetworkHost virt)
            {
                return ($"Magic Packet to \"{host.Name}\" at \"{virt.PhysicalHost.Name}\"");
            }
            else
            {
                return ($"Magic Packet to \"{host.Name}\"");
            }
        }

        public static async Task<string> ToDescription(this DemandEvent @event)
        {
            return $"{@event.Host.ToTarget()}, triggered by {await @event.Host.Network.ToTrigger(@event.TriggerPacket)}";
        }
    }
}
