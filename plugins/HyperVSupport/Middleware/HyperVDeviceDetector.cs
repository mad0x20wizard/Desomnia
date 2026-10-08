using Autofac;
using Autofac.Core.Resolving.Pipeline;
using MadWizard.Desomnia.Network.HyperV.Configuration;
using MadWizard.Desomnia.Network.HyperV.Manager;
using MadWizard.Desomnia.Network.HyperV.Middleware;
using MadWizard.Desomnia.Network.Manager;
using Microsoft.Extensions.Logging;
using Microsoft.Management.Infrastructure;
using SharpPcap;

namespace MadWizard.Desomnia.Network.HyperV
{
    public sealed class HyperVDeviceDetector : IResolveMiddleware
    {
        public PipelinePhase Phase => PipelinePhase.ParameterSelection;

        public VirtualTraffic WatchVirtualTraffic { get; init; } = VirtualTraffic.Internal | VirtualTraffic.External;

        public void Execute(ResolveRequestContext context, Action<ResolveRequestContext> next)
        {
            var @interface = context.FirstParameterOfType<INetworkInterface>();
            var device = context.FirstParameterOfType<ILiveDevice>();

            if (@interface is not null && device is not null)
            {
                var logger = context.Resolve<ILogger<HyperVDeviceDetector>>();

                try
                {
                    var @switch = context.Resolve<HyperVManager>().FindSwitch(@interface.Identity);

                    if (@switch?.Type == HyperVSwitchType.External) // bridged
                    {
                        logger.LogDebug("The network device '{device}' is a virtual switch in bridged mode.", device.Description);

                        if (@switch.QueryPhysicalInterface() is { } physical)
                        {
                            if (CaptureDeviceList.Instance.TryFindByIdentity(physical.Identity, out var physicalDevice))
                            {
                                if (!ReferenceEquals(device, physicalDevice))
                                {
                                    ILiveDevice composite = new HyperVDevice(context.Resolve<ILogger<HyperVDevice>>(),
                                        device, physicalDevice, @interface.PhysicalAddress, @switch.QueryVirtualMachineAddresses())
                                    {
                                        WatchVirtualTraffic = WatchVirtualTraffic
                                    };

                                    context.ChangeParameterByType(composite);

                                    logger.LogDebug("Hyper-V capture mode: '{mode}'; '{virtual}' -> '{physical}'",
                                        ToModeString(WatchVirtualTraffic), device.Description, physicalDevice.Description);
                                }
                            }
                            else
                            {
                                logger.LogWarning("No Npcap device was found for physical interface '{physical}' ({id}). " +
                                    "The Npcap filter binding may not be installed or enabled on this interface.", physical.Name, physical.Identity.Id);
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is CimException or InvalidOperationException or NotSupportedException or ArgumentException)
                {
                    logger.LogWarning(ex, "Could not configure Hyper-V capture for '{interface}'; keeping capture device '{device}'",
                        @interface.Name, device.Description);
                }
            }

            next(context);
        }

        static string ToModeString(VirtualTraffic mode)
        {
            List<string> modes = [];
            if (mode.HasFlag(VirtualTraffic.Private))
                modes.Add("private");
            if (mode.HasFlag(VirtualTraffic.Internal))
                modes.Add("internal");
            if (mode.HasFlag(VirtualTraffic.External))
                modes.Add("external");

            return modes.Count > 0 ? string.Join('|', modes) : "none";
        }
    }
}
