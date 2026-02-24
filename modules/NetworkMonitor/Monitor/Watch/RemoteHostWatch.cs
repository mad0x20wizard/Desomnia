using Autofac;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Demand;
using MadWizard.Desomnia.Network.Impersonation;
using MadWizard.Desomnia.Network.Knocking;
using MadWizard.Desomnia.Network.Reachability;
using MadWizard.Desomnia.Ressource.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using PacketDotNet;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Timers;

using PingOptions = MadWizard.Desomnia.Network.Configuration.Options.PingOptions;

using Timer = System.Timers.Timer;

namespace MadWizard.Desomnia.Network
{
    public class RemoteHostWatch : HostDemandWatch
    {
        public required ReachabilityService Reachability { protected get; init; }
        public required ReachabilityCache ReachabilityCache { protected get; init; }
        public required AddressMappingService AddressMapping { private get; init; }
        public required KnockService Knocking { private get; init; }

        public required PingOptions PingOptions { get; init; }
        public required WakeOptions WakeOptions { get; init; }

        public bool IsSuspended { get; private set; } = false;

        public DateTime? LastSeen   { get; internal set { Seen?.Invoke(this,    value ?? throw new ArgumentNullException(nameof(LastSeen)));     field = value; } }
        public DateTime? LastUnseen { get; internal set { Unseen?.Invoke(this,  value ?? throw new ArgumentNullException(nameof(LastUnseen)));   field = value; } }
        public DateTime? LastWoken  { get; internal set { Woken?.Invoke(this,   value ?? throw new ArgumentNullException(nameof(LastWoken)));    field = value; } }

        public event EventHandler<DateTime>? Seen;
        public event EventHandler<DateTime>? Unseen;
        public event EventHandler<DateTime>? Woken;

        public event EventInvocation? UnMagicPacket;

        private Timer? _pingTimer;
        private readonly AsyncLock _pingLock = new();

        public RemoteHostWatch()
        {
            Seen += RemoteHostWatch_Seen;
            Unseen += RemoteHostWatch_Unseen;

            MonitoringStarted += RemoteHostWatch_TrackingStarted;
            UnMagicPacket += RemoteHostWatch_UnMagicPacket;
            MonitoringStopped += RemoteHostWatch_TrackingStopped;

            Suspended += async (@event) => HandleOffline(true);
            Stopped += async (@event) => HandleOffline(false);
        }

        public override bool IsOnline => this.HasBeenSeen(PingOptions.Timeout);

        protected override bool ShouldStartRequest(EthernetPacket packet)
        {
            if (!this.HasBeenWokenSince(WakeOptions.Timeout))
            {
                return base.ShouldStartRequest(packet);
            }

            return false;
        }

        private async Task DetermineIfHostCanBeSeen(bool hasSuspended = false)
        {
            using var mutex = await _pingLock.LockAsync();

            _pingTimer?.Stop(); // stop timer to reduce concurrency

            try
            {
                if (await Reachability.Test(this, label: "remote host"))
                {
                    LastSeen = DateTime.Now;
                }
                else
                {
                    if (hasSuspended)
                    {
                        IsSuspended = true;
                    }

                    LastUnseen = DateTime.Now;
                }
            }
            finally
            {
                _pingTimer?.Start();
            }
        }

        protected override void HandleMagicPacket(EthernetPacket packet)
        {
            if (packet.IsUnMagicPacket())
            {
                Logger.LogDebug($"Received UnMagic Packet from \"{Host.Name}\", " +
                    $"triggered by {packet.SourceHardwareAddress.ToHexString()}");

                TriggerEvent(nameof(UnMagicPacket));
            }
            else // was woken up by an external magic packet
            {
                LastWoken = DateTime.Now;

                base.HandleMagicPacket(packet);

                if (!WakeOptions.Silent) _ = Logger.LogEvent(null, "Observed", Host, packet);
            }
        }

        protected void HandleOffline(bool suspended)
        {
            var ips = Host.IPAddresses.Where(suspended ? DemandOptions.ShouldAdvertiseOnRemoteHostSuspended : DemandOptions.ShouldAdvertiseOnRemoteHostStopped);
            
            if (ips.Any())
            {
                Logger.LogDebug($"Taking ownership of IP addresses...");

                foreach (var ip in ips)
                {
                    AddressMapping.Advertise(new(ip, Device.PhysicalAddress)); // no need to recheck reachability here
                }
            }
        }

        internal protected override async Task<PhysicalAddress?> RequestIPUnicastTrafficTo(IPAddress ip)
        {
            if (!await Reachability.Test(this, ip, label: "remote host"))
            {
                return Device.PhysicalAddress;
            }

            return null;
        }

        #region Waking
        [ActionHandler("wake")]
        public virtual async Task Wake(DemandEvent @event)
        {
            if (WakeOptions.Type == WakeType.None)
            {
                Logger.LogDebug($"Did not try to wake '{Host.Name}', because waking is disabled."); return;
            }

            LastWoken = DateTime.Now;

            if (!await Reachability.Test(this, @event.TargetEndPoint?.Address)) // is host sleeping at all?
            {
                var stopwatch = Stopwatch.StartNew();

                try
                {
                    await WakeUp(@event.TargetEndPoint?.Address);

                    if (DemandOptions.ShouldForward(@event))
                    {
                        ForwardPackets(@event);
                    }

                    if (!WakeOptions.Silent) await Logger.LogWake(@event, stopwatch.Elapsed);
                }
                catch (HostTimeoutException)
                {
                    await Logger.LogWakeTimeout(@event, stopwatch.Elapsed);
                }
            }
        }

        protected internal virtual async Task WakeUp(IPAddress? ip = null)
        {
            using PeriodicTimer timer = new(WakeOptions.Repeat ?? WakeOptions.Timeout * 2);

            async void PeriodicSendMagicPacket()
            {
                do SendMagicPacket(ip); while (await timer.WaitForNextTickAsync());
            }

            PeriodicSendMagicPacket();

            await Reachability.MaybePingUntil(this, WakeOptions.Timeout);
        }

        private int SendMagicPacket(IPAddress? hint = null)
        {
            var wol = new WakeOnLanPacket(Host.PhysicalAddress ?? throw new HostAbortedException($"Host '{Host.Name}' has no PhysicalAddress configured."));

            int countPackets = 0;
            var wakeType = WakeOptions.Type;
            foreach (var ip in (hint != null ? [hint] : Host.IPAddresses.ToArray()))
            {
                if (wakeType == WakeType.Auto && !Host.Network.LocalRange.Contains(ip) || wakeType.HasFlag(WakeType.Network))
                {
                    using UdpClient udp = new(ip.AddressFamily);

                    Logger.LogTrace($"Wake up \"{Host.Name}\" at {ip} using {WakeOptions.Port}/udp ");

                    var bytes = udp.Send(wol.Bytes, new IPEndPoint(ip, WakeOptions.Port));

                    LastWoken = DateTime.Now;

                    countPackets++;
                }
            }

            if (wakeType == WakeType.Auto && countPackets == 0 || WakeOptions.Type.HasFlag(WakeType.Link))
            {
                var sourceMAC = Device.PhysicalAddress;
                var targetMAC = wakeType.HasFlag(WakeType.Unicast) ? Host.PhysicalAddress : PhysicalAddressExt.Broadcast;

                Logger.LogTrace($"Wake up '{Host.Name}' at {Host.PhysicalAddress.ToHexString()}");

                Device.SendPacket(new EthernetPacket(sourceMAC, targetMAC, EthernetType.WakeOnLan)
                {
                    PayloadPacket = wol
                });

                LastWoken = DateTime.Now;

                countPackets++;
            }

            return countPackets;
        }
        #endregion

        #region Knocking
        [ActionHandler("knock")]
        public async Task Knock(DemandEvent @event)
        {
            if (this[@event.Service] is not NetworkServiceWatch watch || watch.KnockOptions is not KnockOptions options)
                { Logger.LogWarning($"Cannot knock at '{Host.Name}', because no NetworkService / KnockOptions is available."); return; }

            if (@event.SourceAddress is not IPAddress source)
                { Logger.LogWarning($"Cannot knock at '{Host.Name}', because no source IP is available."); return; }
            if (@event.TargetEndPoint is not IPEndPoint target)
                { Logger.LogWarning($"Cannot knock at '{Host.Name}', because no target IP endpoint is available."); return; }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (await KnockUp(source, target, options))
                {
                    // TODO: Forward packets while knocking?
                    //if (DemandOptions.ShouldForward(@event))
                    //{
                    //    ForwardPackets(@event);
                    //}

                    await Logger.LogKnock(@event, stopwatch.Elapsed);
                }
            }
            catch (HostTimeoutException)
            {
                await Logger.LogKnockTimeout(@event, stopwatch.Elapsed);
            }
        }

        protected internal virtual async Task<bool> KnockUp(IPAddress source, IPEndPoint target, KnockOptions options)
        {
            if (ReachabilityCache.Read(target, options.Delay) == true)
            {
                Logger.LogDebug($"Did not knock at '{Host.Name}' ({target}), because it is already reachable.");
                return false;
            }

            int knocked = 0;

            using PeriodicTimer timer = new(options.Repeat ?? options.Timeout * 2);

            async void PeriodicKnocking(CancellationToken token)
            {
                try
                {
                    await Task.Delay(options.Delay, token);

                    do
                    {
                        await Knocking.DoSinglePacketAuthorization(options.Method, source, target, options.Port, options.Secret);

                        knocked++;
                    }
                    while (await timer.WaitForNextTickAsync(token));
                }
                catch (OperationCanceledException) { }
            }

            using CancellationTokenSource cancellation = new();

            PeriodicKnocking(cancellation.Token);

            try
            {
                await Reachability.Until(new ServiceReachabilityTest(target, options.Timeout));

                return knocked > 0;
            }
            finally
            {
                cancellation.Cancel();
            }
        }
        #endregion

        #region Triggers for Ping
        private void RemoteHostWatch_TrackingStarted(object? sender, MonitorEventArgs args)
        {
            if (PingOptions.Frequency is TimeSpan interval && interval > TimeSpan.Zero)
            {
                _pingTimer = new Timer(interval);
                _pingTimer.Elapsed += Timer_Elapsed;
                _pingTimer.AutoReset = true;
                _pingTimer.Start();
            }
        }

        private async void Timer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (!HasOngoingRequests)
            {
                using var scope = Logger.BeginHostScope(Host);

                Logger.LogTrace($"Time for periodic reachability check; scheduled every {PingOptions.Frequency}");

                await DetermineIfHostCanBeSeen();
            }
        }

        private async Task RemoteHostWatch_UnMagicPacket(Event @event)
        {
            _pingTimer?.Stop();

            _ = Task.Run(async() =>
            {
                await Task.Delay(DemandOptions.Timeout);

                await DetermineIfHostCanBeSeen(true);
            });
        }

        private void RemoteHostWatch_TrackingStopped(object? sender, MonitorEventArgs args)
        {
            _pingTimer?.Stop();
            _pingTimer = null;
        }
        #endregion

        #region Triggers for Started/Suspended/Stopped events
        private void RemoteHostWatch_Seen(object? sender, DateTime time)
        {
            using var scope = Logger.BeginHostScope(Host);

            IsSuspended = false;

            if (LastUnseen != null)
            {
                if (LastSeen == null || LastSeen < LastUnseen)
                {
                    TriggerStarted();
                }
            }
        }

        private void RemoteHostWatch_Unseen(object? sender, DateTime time)
        {
            using var scope = Logger.BeginHostScope(Host);

            if (LastSeen != null)
            {
                if (LastUnseen == null || LastUnseen < LastSeen)
                {
                    if (IsSuspended)
                    {
                        TriggerSuspended();
                    }
                    else
                    {
                        TriggerStopped();
                    }
                }
            }
        }
        #endregion
    }
}
