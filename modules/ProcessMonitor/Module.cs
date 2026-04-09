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
            var manager = Config.ProcessMonitor as ProcessManagerConfig;

            // fallback, if no better ProcessManager is available
            builder.RegisterType<PollingProcessManager>().As<ProcessManager>().AsImplementedInterfaces().AsSelf()
                .WithParameter(TypedParameter.From(manager?.PollInterval ?? ProcessManagerConfig.DefaultPollInterval))
                .OnlyIf(reg => !reg.IsRegistered(new TypedService(typeof(IProcessManager))))
                .SingleInstance();

            if (Config.ProcessMonitor is ProcessMonitorConfig config)
            {
                builder.RegisterType<ProcessMonitor>()
                    .WithParameter(new TypedParameter(typeof(ProcessMonitorConfig), config))
                    .AsImplementedInterfaces()
                    .SingleInstance()
                    .AsSelf();

                builder.RegisterType<ProcessWatch>().AsSelf();
            }
        }
    }
}
