using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Demand;
using MadWizard.Desomnia.Network.Manager;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Watch
{
    internal class LocalVirtualHostWatch : LocalHostWatch, IDisposable
    {
        private IVirtualMachine VM { get; }

        public override bool IsOnline => VM.State == VirtualMachineState.Running;

        /**
         * If we don't allow at least 2 concurrent requests for local virtual hosts,
         * it can lead to a race condition with a Sleep Proxy in promiscuous mode,
         * resulting in the Sleep Proxy to take over the IP of the virtual host,
         * while this should be the responsibility of the physical host.
         */
        protected override int MaxConcurrentRequests => Math.Max(base.MaxConcurrentRequests, AdvertiseOptions.Type != AdvertiseType.Never ? 2 : 0);

        public LocalVirtualHostWatch(IVirtualMachine vm)
        {
            (VM = vm).StateChanged += VM_StateChanged;

            Started += async (@event) => HandleStarted();
            Suspended += async (@event) => HandleSuspended();
        }

        protected internal override async Task StartWatch()
        {
            await base.StartWatch();

            if (!IsOnline && AdvertiseOptions.Type == AdvertiseType.Never)
            {
                try
                {
                    await HandoffWatch();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Could not handoff watch for '{Host}'.", Host.Name);
                }
            }
        }

        private async Task ReclaimAddresses(IEnumerable<IPAddress> addresses)
        {
            if (addresses.Any()) using (Logger.BeginHostScope(Host))
            {
                Logger.LogDebug($"Reclaiming ownership of local virtual IP addresses...");

                foreach (var ip in addresses)
                {
                    try
                    {
                        if (await RequestIPUnicastTrafficTo(ip) is PhysicalAddress mac)
                        {
                            AddressMapping.Advertise(new(ip, mac),
                                // need to send from host address, so that any sleep proxy
                                // registers this as host activity and an end the lease
                                source: Host.PhysicalAddress);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Could not reclaim IP for '{Host}'.", Host.Name);
                    }
                }
            }
        }

        protected internal override async Task ReclaimWatch() => await ReclaimWatch(false);

        private async Task ReclaimWatch(bool force)
        {
            if (!force && AdvertiseOptions.Type == AdvertiseType.Never && !IsOnline)
                return;

            if (!IsOnline)
            {
                if (_handoffDone && SleepProxyRegistrationCycle > 0)
                {
                    await ReclaimAddresses(Host.SelectIPAddressesBy(HandoffOptions));
                }
                else
                {
                    await ReclaimAddresses(Host.IPAddresses.Where(AdvertiseOptions.ShouldAdvertiseOnLocalHostResume));
                }
            }

            await base.ReclaimWatch();
        }


        protected override bool ShouldTriggerEvent(Event @event)
        {
            if (@event.Type == nameof(Idle) && IsOnline != true)
                return false; // only trigger "Idle" events if the VM is running

            if (@event.Type == nameof(Demand) && (IsOnline == true || @event is InspectionEvent))
                return false; // only trigger "Demand" events if the VM is NOT running

            return base.ShouldTriggerEvent(@event);
        }

        private void VM_StateChanged(object? sender, VirtualMachineStateChangedEventArgs args)
        {
            switch (args.State)
            {
                case VirtualMachineState.Running:
                    TriggerStarted();
                    break;
                case VirtualMachineState.Suspended:
                    TriggerSuspended();
                    break;
                case VirtualMachineState.Stopped:
                    TriggerStopped();
                    break;
            }
        }

        protected override void HandleMagicPacket(EthernetPacket packet)
        {
            if (!IsOnline) // ignore if VM is online
            {
                base.HandleMagicPacket(packet);
            }
        }

        private async void HandleStarted()
        {
            await ReclaimWatch();
        }

        private async void HandleSuspended()
        {
            if (AdvertiseOptions.Type == AdvertiseType.Never)
            {
                try
                {
                    await HandoffWatch();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Could not handoff watch of local virtual host '{host}'", Host.Name);
                }
            }
        }

        protected internal override async Task StopWatch(bool gracefully)
        {
            if (gracefully)
            {
                await ReclaimWatch(true);
            }
        }

        internal protected override async Task<PhysicalAddress?> RequestIPUnicastTrafficTo(IPAddress ip)
        {
            return Host.PhysicalAddress;
        }

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            var tokens = base.InspectResource(interval).ToHashSet();

            /**
             * On some platforms, a virtual machine can be accesses by other means
             * than via a network socket. To prevent these from treated as idle,
             * we also include the VM's intrinsic usage tokens.
             */
            if (VM is IInspectable vm && vm.Inspect(interval) is var vmTokens && vmTokens.Any())
            {
                // create NetworkHostUsage, if not present
                if (tokens.OfType<NetworkHostUsage>().FirstOrDefault() is not NetworkHostUsage usage)
                    tokens.Add(usage = new NetworkHostUsage(Host, 0));

                foreach (var token in vmTokens)
                    usage.Tokens.Add(token);
            }

            return tokens;
        }

        #region VM action handlers
        [ActionHandler("wake")]
        public virtual async Task Wake(DemandEvent @event)
        {
            if (VM.State == VirtualMachineState.Suspended)
            {
                await this.Start(@event);
            }
        }

        [ActionHandler("start")]
        public virtual async Task Start(DemandEvent @event)
        {
            if (VM.State != VirtualMachineState.Running)
            {
                await VM.Start();

                if (DemandOptions.ShouldForward(@event))
                {
                    ForwardPackets(@event);
                }
            }
        }

        [ActionHandler("suspend")]
        public virtual async Task Suspend()
        {
            if (VM.State == VirtualMachineState.Running)
            {
                using var scope = Logger.BeginHostScope(Host);

                await VM.Suspend();
            }
        }

        [ActionHandler("stop")]
        public virtual async Task Stop()
        {
            if (VM.State == VirtualMachineState.Running)
            {
                using var scope = Logger.BeginHostScope(Host);

                await VM.Stop();
            }
        }
        #endregion

        public override void Dispose()
        {
            VM.StateChanged -= VM_StateChanged;

            base.Dispose();
        }
    }
}
