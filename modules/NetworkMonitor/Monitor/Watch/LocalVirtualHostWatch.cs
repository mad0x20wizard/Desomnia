using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Demand;
using MadWizard.Desomnia.Network.Manager;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;

namespace MadWizard.Desomnia.Network
{
    internal class LocalVirtualHostWatch : LocalHostWatch, IDisposable
    {
        private IVirtualMachine VM { get; }

        public override bool IsOnline => VM.State == VirtualMachineState.Running;

        //protected override int MaxConcurrentRequests => 2;

        public LocalVirtualHostWatch(IVirtualMachine vm)
        {
            (VM = vm).StateChanged += VM_StateChanged;

            Suspended += async (@event) => HandleSuspended();
        }

        private void VM_StateChanged(object? sender, VirtualMachineStateChangedEventArgs args)
        {
            using var scope = Logger.BeginHostScope(Host);

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
