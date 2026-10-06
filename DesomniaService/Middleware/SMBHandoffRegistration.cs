using Autofac;
using Autofac.Core.Resolving.Pipeline;
using MadWizard.Desomnia.Network.Handoff.Registration;
using MadWizard.Desomnia.Network.Middleware;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Watch;
using System.Net;

namespace MadWizard.Desomnia.NetworkSession
{
    public sealed class SMBHandoffRegistration : HandoffServiceRegistration
    {
        protected override IEnumerable<HandoffServiceInfo> HandoffServices(ResolveRequestContext context, LocalHostWatch watch)
        {
            if (context.ResolveOptional<NetworkSessionMonitor>() is not null && watch.Host is LocalHost)
            {
                yield return new HandoffServiceInfo(watch.AdvertiseOptions)
                {
                    Name = "SMB",
                    ServiceName = "smb",

                    Protocol = IPProtocol.TCP,
                    Port = 445,
                };
            }
        }
    }
}
