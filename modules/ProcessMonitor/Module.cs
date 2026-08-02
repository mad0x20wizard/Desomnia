using Autofac;
using MadWizard.Desomnia.Processes.Configuration;

namespace MadWizard.Desomnia.Processes
{
    public class Module : Desomnia.ConfigurableModule<ModuleConfig>
    {
        protected override void Load(ContainerBuilder builder, ModuleConfig config)
        {
            if (config.ProcessMonitor is ProcessMonitorConfig monitor)
            {
                builder.RegisterType<ProcessMonitor>()
                    .WithParameter(new TypedParameter(typeof(ProcessMonitorConfig), monitor))
                    .AsImplementedInterfaces()
                    .SingleInstance()
                    .AsSelf();

                builder.RegisterType<ProcessWatch>().AsSelf();
            }
        }
    }
}
