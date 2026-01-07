using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.NetworkSession.Configuration;
using MadWizard.Desomnia.NetworkSession.Manager;

namespace MadWizard.Desomnia.NetworkSession
{
    public class Module : Desomnia.ConfigurableModule<ModuleConfig>
    {
        protected override void Load(ContainerBuilder builder)
        {
            if (Config.NetworkSessionMonitor is not null)
            {
                builder.RegisterType<NetworkSessionMonitor>()
                    .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(INetworkSessionManager))))
                    .WithParameter(TypedParameter.From(Config.NetworkSessionMonitor.FilterRule))
                    .AsImplementedInterfaces()
                    .SingleInstance()
                    .AsSelf();
            }
        }
    }
}
