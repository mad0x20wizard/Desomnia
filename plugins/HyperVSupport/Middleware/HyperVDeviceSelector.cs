using Autofac;
using Autofac.Core.Resolving.Pipeline;
using Microsoft.Extensions.Logging;
using SharpPcap;
using SharpPcap.LibPcap;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.HyperV
{
    public sealed class HyperVDeviceSelector : IResolveMiddleware
    {
        public PipelinePhase Phase => PipelinePhase.ParameterSelection;

        public void Execute(ResolveRequestContext context, Action<ResolveRequestContext> next)
        {
            var logger = context.Resolve<ILogger<HyperVDeviceSelector>>();

            var @interface = context.FirstParameterOfType<NetworkInterface>();
            var device = context.FirstParameterOfType<ILiveDevice>();

            if (@interface is not null && device is not null && device.MacAddress is PhysicalAddress mac)
            {
                if (device.Description.Contains("Hyper-V"))
                {
                    var siblings = CaptureDeviceList.Instance.Where(dev => dev != device && mac.Equals(dev.MacAddress));

                    if (siblings.OfType<LibPcapLiveDevice>().Where(dev => dev.Addresses.Count == 1).FirstOrDefault() is ILiveDevice physical)
                    {
                        context.ChangeParameterByType(physical);

                        logger.LogDebug("Switched virtual device '{virtual}' to physical device '{physical}', to monitor entire VM traffic", 
                            device.Description, physical.Description);
                    }
                }
            }

            next(context);
        }
    }
}
