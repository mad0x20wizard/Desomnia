using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Demand;
using MadWizard.Desomnia.Network.Manager;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network
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
        protected override int MaxConcurrentRequests => Math.Max(base.MaxConcurrentRequests, DemandOptions.Advertise != AddressAdvertisment.Never ? 2 : 0);

        public LocalVirtualHostWatch(IVirtualMachine vm)
        {
            (VM = vm).StateChanged += VM_StateChanged;

            Suspended += async (@event) => HandleSuspended();
        }

        protected override Task TriggerEventAsync(Event @event)
        {
            if (@event.Type == nameof(Idle) && IsOnline != true)
                return Task.CompletedTask; // only trigger "Idle" events if the VM is running

            if (@event.Type == nameof(Demand) && (IsOnline == true || @event is InspectionEvent))
                return Task.CompletedTask; // only trigger "Demand" events if the VM is NOT running

            return base.TriggerEventAsync(@event);
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

        private void HandleSuspended()
        {
            if (DemandOptions.Advertise == AddressAdvertisment.Never)
            {
                YieldWatch();
            }
        }

        internal protected override async Task<PhysicalAddress?> RequestIPUnicastTrafficTo(IPAddress ip)
        {
            return Host.PhysicalAddress;
        }

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

        public override void Dispose()
        {
            VM.StateChanged -= VM_StateChanged;

            base.Dispose();
        }
    }
}
