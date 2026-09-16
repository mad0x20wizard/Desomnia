using Autofac;
using Autofac.Core.Resolving.Pipeline;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.SleepProxy.Registration;
using MadWizard.Desomnia.Network.Watch;
using System.Net;

namespace MadWizard.Desomnia.Session.Middleware
{
    public sealed class RDPSleepProxyRegistration : IResolveMiddleware
    {
        public PipelinePhase Phase => PipelinePhase.ParameterSelection;

        public void Execute(ResolveRequestContext context, Action<ResolveRequestContext> next)
        {
            next(context);

            if (context.FirstParameterOfType<LocalHostWatch>() is LocalHostWatch watch && watch.Host is LocalHost)
            {
                if (context.Instance is SleepProxyRegistration reg)
                    reg.Services.Add(new ProxyServiceInfo(watch.AdvertiseOptions)
                    {
                        Name = "RDP",
                        ServiceName = "rdp", // use "ms-wbt-server" ??

                        Protocol = IPProtocol.TCP,
                        Port = 3389,
                    });
            }
        }
    }
}
