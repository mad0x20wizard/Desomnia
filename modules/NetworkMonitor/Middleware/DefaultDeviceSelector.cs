using Autofac;
using Autofac.Core.Resolving.Pipeline;
using SharpPcap;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Middleware
{
    public sealed class DefaultDeviceSelector : IResolveMiddleware
    {
        public PipelinePhase Phase => PipelinePhase.ParameterSelection;

        public void Execute(ResolveRequestContext context, Action<ResolveRequestContext> next)
        {
            var @interface = context.FirstParameterOfType<NetworkInterface>()!;
            
            if (context.FirstParameterOfType<ILiveDevice>() is null)
            {
                var device = FindDeviceByID(@interface) ?? throw new FileNotFoundException($"Network interface with name \"{@interface.Name}\" not found.");

                context.ChangeParameters([..context.Parameters, TypedParameter.From(device)]);
            }

            next(context);
        }

        private static ILiveDevice? FindDeviceByID(NetworkInterface @interface)
        {
            CaptureDeviceList.Instance.Refresh();

            return CaptureDeviceList.Instance.Where(device => device.Name.Contains(@interface.Id)).FirstOrDefault();
        }
    }
}
