using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Process.Configuration;
using MadWizard.Desomnia.Process.Manager;

namespace MadWizard.Desomnia.Process
{
    public class Module : Desomnia.ConfigurableModule<ModuleConfig>
    {
        protected override void Load(ContainerBuilder builder)
        {
            if (Config.ProcessMonitor is ProcessMonitorConfig config)
            {
                builder.RegisterType<ProcessMonitor>()
                    .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(IProcessManager))))
                    .WithParameter(new TypedParameter(typeof(ProcessMonitorConfig), config))
                    .AsImplementedInterfaces()
                    .SingleInstance()
                    .AsSelf();
            }
        }
    }
}
