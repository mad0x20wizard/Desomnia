using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.NetworkSession.Configuration;
using MadWizard.Desomnia.NetworkSession.Manager;

namespace MadWizard.Desomnia.NetworkSession
{
    public class Module : Desomnia.ConfigurableModule<ModuleConfig>
    {
        protected override void Load(ContainerBuilder builder, ModuleConfig config)
        {
            if (config.NetworkSessionMonitor is NetworkSessionMonitorConfig monitor)
            {
                builder.RegisterType<NetworkSessionMonitor>()
                    .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(INetworkSessionManager))))
                    .WithParameter(TypedParameter.From(monitor.MakeWatchOptions()))
                    .WithParameter(TypedParameter.From(monitor.FilterRule))
                    .AsImplementedInterfaces()
                    .SingleInstance()
                    .AsSelf();
            }
        }
    }
}
