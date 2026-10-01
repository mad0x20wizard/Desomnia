using Autofac.Core.Resolving.Pipeline;
using MadWizard.Desomnia.Network.Middleware;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.SleepProxy.Registration;
using MadWizard.Desomnia.Network.Watch;
using System.Net;

namespace MadWizard.Desomnia.Session.Middleware
{
    public sealed class RDPSleepProxyRegistration : SleepProxyServiceRegistration
    {
        protected override IEnumerable<ProxyServiceInfo> RegisterProxyServices(ResolveRequestContext context, LocalHostWatch watch)
        {
            if (watch.Host is LocalHost)
            {
                yield return new ProxyServiceInfo(watch.AdvertiseOptions)
                {
                    Name = "RDP",
                    ServiceName = "rdp", // use "ms-wbt-server" ??

                    Protocol = IPProtocol.TCP,
                    Port = 3389,
                };
            }
        }
    }
}
