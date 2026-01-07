using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Power.Manager;
using MadWizard.Desomnia.PowerRequest.Configuration;

namespace MadWizard.Desomnia.PowerRequest
{
    public class Module : Desomnia.ConfigurableModule<ModuleConfig>
    {
        protected override void Load(ContainerBuilder builder)
        {
            if (Config.PowerRequestMonitor is PowerRequestMonitorConfig config)
            {
                builder.RegisterType<PowerRequestMonitor>()
                    .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(IPowerManager))))
                    .WithParameter(new TypedParameter(typeof(PowerRequestMonitorConfig), config))
                    .AsImplementedInterfaces()
                    .SingleInstance()
                    .AsSelf();
            }
        }
    }
}
