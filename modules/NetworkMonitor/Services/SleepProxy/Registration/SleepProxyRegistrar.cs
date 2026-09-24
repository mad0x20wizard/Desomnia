using Autofac;
using Autofac.Core;
using Autofac.Features.OwnedInstances;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Reachability;
using MadWizard.Desomnia.Network.Watch;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.SleepProxy.Registration
{
    internal class SleepProxyRegistrar(AutoDiscoveryType auto, SleepProxyOptions options)
    {
        public required ILogger<SleepProxyRegistrar> Logger { private get; init; }

        public required NetworkContext Context { private get; set; }

        public required ReachabilityService Reachability { private get; set; }

        public required Func<SleepProxyRegistration, TimeSpan, Owned<SleepProxyLease>> CreateLease { private get; init; }

        readonly ConcurrentDictionary<PhysicalAddress, Owned<SleepProxyLease>> _activeLeases = [];

        public bool Register(SleepProxyRegistration reg, out SleepProxyLease lease)
        {
            var duration = options.ClampLeaseDuration(reg.RequestedLease);

            if (_activeLeases.Count >= options.LeaseLimit)
                throw new InvalidOperationException($"Lease pool ({_activeLeases.Count}) exhausted.");

            if (!_activeLeases.TryGetValue(reg.PrimaryAddress, out var owned))
            {
                Logger.LogDebug("Attempt to register '{Name}' [{Sequence}] at {PhysicalAddress} with {ServiceCount} service(s) and {AddressCount} address(es)...",
                    reg.Name, reg.Sequence, reg.PrimaryAddress.ToHexString(), reg.Services.Count, reg.IPAddresses.Count);

                lease = (owned = CreateLease(reg, duration)).Value;
            }
            else if ((lease = owned.Value).Registration.Sequence < reg.Sequence)
            {
                Logger.LogDebug("Attempt to re-register '{Name}' [{NewSequence} > {OldSequence}] at {PhysicalAddress} with {ServiceCount} service(s) and {AddressCount} address(es)... ",
                    reg.Name, reg.Sequence, owned.Value.Registration.Sequence, reg.PrimaryAddress.ToHexString(), reg.Services.Count, reg.IPAddresses.Count);

                owned.Dispose(); owned = null;

                _activeLeases.Remove(reg.PrimaryAddress, out _);

                lease = (owned = CreateLease(reg, duration)).Value;
            }
            else
            {
                // A registration we've already seen (sequence <= the held lease). Exact-duplicate messages are filtered
                // out by the resolver, so this is a re-send the host expects us to re-acknowledge -- as long as it still
                // describes the same registration we hold; otherwise it's a conflict.
                if (!lease.Registration.Matches(reg))
                    throw new InvalidOperationException(
                        $"Registration of '{reg.Name}' [{reg.Sequence}] " +
                        $"at {reg.PrimaryAddress.ToHexString()} " +
                        $"conflicts with the held lease.");

                return false;
            }

            try
            {
                if ((Context.FindHostContextBy(reg.PrimaryAddress) ?? Context.FindHostContextBy(reg.Name)) is not NetworkHostContext ctxHost)
                {
                    ctxHost = CreateHost(reg);

                    lease.AddInstanceForDisposal(ctxHost);
                }

                using (Logger.BeginHostScope(ctxHost.Host))
                {
                    Logger.LogDebug("Configuring host '{Host}' for sleep proxy handoff:", ctxHost.Host.Name);
                }

                if (ctxHost.Watch is not RemoteHostWatch remote)
                    throw new NotSupportedException("Service registration is only supported for watched remote hosts.");

                remote.LastSeen = DateTime.Now;

                if (ctxHost.Auto.HasFlag(AutoDiscoveryType.Service))
                {
                    foreach (var serviceInfo in reg.Services)
                    {
                        if (ctxHost.FindServiceContextBy(serviceInfo.IPPort) is NetworkServiceContext ctxService)
                            throw new NotSupportedException($"Already watching service at {serviceInfo.IPPort} for {ctxHost.Host.Name}.");

                        ctxService = ctxHost.CreateWatchedService(serviceInfo);

                        lease.AddInstanceForDisposal(ctxService);
                    }
                }
                else if (reg.Services.Count > 0)
                {
                    throw new NotSupportedException($"Registration of services is not configured for {ctxHost.Host.Name}.");
                }

                _activeLeases[reg.PrimaryAddress] = owned;

                var filterHosts = Context.CreateDynamicFilterHosts().ToList(); // the remote host may register dynamic host filters

                Task.Run(async () => // this is time consuming and not relevant for the DNS response, so let's decouple it
                {
                    using var scope = Logger.BeginHostScope(remote.Host);

                    var lease = _activeLeases[reg.PrimaryAddress].Value;

                    if (ctxHost.Auto.HasFlag(AutoDiscoveryType.MAC) && ctxHost.Host.PhysicalAddress is null) using (await Context.Network.Mutex.LockAsync())
                    {
                        ctxHost.Host.PhysicalAddress = reg.PrimaryAddress;

                        lease.AddInstanceForDisposal(new SleepProxyPhysicalAddressLease(ctxHost.Host, reg.PrimaryAddress));

                        Logger.LogHostPhysicalAddressChanged(ctxHost.Host, reg.PrimaryAddress);
                    }

                    foreach (var adr in reg.IPAddresses.Where(adr => adr.Key.AddressFamily.ShouldDiscover(ctxHost.Auto))) using (await Context.Network.Mutex.LockAsync())
                    {
                        if (ctxHost.Host.AddAddress(adr.Key, adr.Value))
                        {
                            lease.AddInstanceForDisposal(new SleepProxyAddressLease(Logger, ctxHost.Host, adr.Key));

                            Logger.LogHostAddressAdded(ctxHost.Host, adr.Key);
                        }
                    }

                    foreach (var host in filterHosts)
                    {
                        await host.DiscoverAddresses();
                    }

                    return lease;

                }).ContinueWith(t => FinishRegistration(remote, t.Result));
            }
            catch (Exception)
            {
                owned.Dispose(); throw;
            }

            return true;
        }

        private NetworkHostContext CreateHost(SleepProxyRegistration reg)
        {
            List<Parameter> parameters = [];

            RemoteHostInfo hostInfo;
            if (reg.TargetAddress is PhysicalAddress target) // is this a virtual machine?
            {
                hostInfo = new RemoteVirtualHostInfo() { Name = reg.Name };

                var ctxPhysical = Context.FindHostContextBy(target) ?? throw new KeyNotFoundException($"Wake host with MAC = {target} not found.");

                if (!ctxPhysical.Auto.HasFlag(AutoDiscoveryType.Host))
                    throw new NotSupportedException($"Host {ctxPhysical.Host.Name} is not configured to discover virtual hosts.");

                parameters.Add(TypedParameter.From(new RemotePhysicalHostInfo() { Name = ctxPhysical.Host.Name }));
            }
            else
            {
                if (!auto.HasFlag(AutoDiscoveryType.Host))
                    throw new NotSupportedException("Registration of unknown hosts is not configured.");

                hostInfo = new RemotePhysicalHostInfo() { Name = reg.Name };
            }

            hostInfo.AutoDetect = AutoDiscoveryType.IP | AutoDiscoveryType.Host | AutoDiscoveryType.Service;

            hostInfo.HostName = reg.Hostname;
            hostInfo.MAC = reg.PrimaryAddress;

            hostInfo.WakePasswordBytes = reg.Password;

            return Context.CreateHost([ new TypedParameter(hostInfo.GetType(), hostInfo), .. parameters ]);
        }

        #region Lease start/end validation
        private async Task FinishRegistration(RemoteHostWatch watch, SleepProxyLease lease)
        {
            lease.Ended += async (sender, args) =>
            {
                if (_activeLeases.Remove(lease.Registration.PrimaryAddress, out var owned))
                {
                    using var scope = Logger.BeginHostScope(watch.Host);

                    if (args.HasExpired)
                    {
                        await TryToExpireLeaseGracefully(watch);
                    }

                    Logger.LogDebug("Lease for '{Host}' is going to end; releasing held resources...", watch.Host.Name);

                    using (await Context.Network.Mutex.LockAsync())
                    {
                        owned.Dispose();
                    }

                    string msg = (args.HasFailed ? "Handoff from"  : "Lease for") 
                        + " '{Host}' has " 
                        + (args.HasExpired ? "expired" 
                            : args.HasFailed  ? "failed" 
                            : "ended");

                    if (args.HasFailed && args.Timeout is TimeSpan timeout)
                        msg += $"; host was still alive after {Math.Floor(timeout.TotalSeconds)} seconds";

                    Logger.Log(args.HasFailed ? LogLevel.Warning : LogLevel.Debug, msg, watch.Host.Name);
                }
            };

            async Task stop(Event @event) => lease.Stop(SleepProxyLeaseEndReason.HostStarted);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (await watch.ValidateHandoff())
                {
                    using (Logger.BeginHostScope(watch.Host))
                    {
                        Logger.LogDebug("Handoff from {Host} successful; lease granted until: {GrantedUntil}", watch.Host.Name, lease.GrantedUntil);
                    }

                    watch.Started += stop;

                    lease.Disposed += (sender, args) => watch.Started -= stop;

                    return;
                }
            }
            catch (Exception ex)
            {
                using var scope = Logger.BeginHostScope(watch.Host);

                Logger.LogError(ex, "Could not validate handoff from {Host}.", watch.Host.Name);
            }

            lease.Stop(SleepProxyLeaseEndReason.Failed, stopwatch.Elapsed);
        }

        private async Task TryToExpireLeaseGracefully(RemoteHostWatch watch)
        {
            switch (options.LeaseExpire)
            {
                case LeaseExpireAction.None:
                    return;

                case LeaseExpireAction.Wake when !await Reachability.Test(watch):
                    Logger.LogDebug("Lease for '{Host}' is going to expire, but the remote host is not responding; trying to wake...", watch.Host.Name);

                    try
                    {
                        await watch.WakeUp();
                    }
                    catch (HostTimeoutException ex)
                    {
                        Logger.LogWarning("Remote host '{Host}' didn't wake up after {Timeout} s",
                            watch.Host.Name, Math.Ceiling(ex.Timeout.TotalSeconds));
                    }
                    break;
            }
        }
        #endregion
    }
}
