using Autofac;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;
using MadWizard.Desomnia.Processes.Middleware;
using MadWizard.Desomnia.Processes.Watch;

namespace MadWizard.Desomnia.Processes
{
    public class Module : Desomnia.ConfigurableModule<ModuleConfig>
    {
        protected override void Load(ContainerBuilder builder, ModuleConfig config)
        {
            // What this assembly's own fallback process answers for, and nothing about anybody's
            // operating system: a platform states its counters in its own project and registers
            // them first, so this steps aside wherever one did.
            builder.RegisterType<DefaultProcessMetricSupport>()
                .As<IProcessMetricSupport>()
                .PreserveExistingDefaults()
                .SingleInstance();

            // Every watch is asked, as it is built, whether this machine keeps the counters its
            // thresholds name – whichever module registered it, and whichever scope it lives in.
            builder.ComponentRegistryBuilder.Registered += (sender, args) =>
            {
                if (args.ComponentRegistration.IsLimitedTo<ProcessWatch>())
                    args.ComponentRegistration.PipelineBuilding += (_, pipeline) =>
                        pipeline.Use(new ProcessMetricValidation());
            };

            if (config.ProcessMonitor is ProcessMonitorConfig monitor)
            {
                builder.RegisterType<ProcessMonitor>()
                    .WithParameter(new TypedParameter(typeof(ProcessMonitorConfig), monitor))
                    .AsImplementedInterfaces()
                    .SingleInstance()
                    .AsSelf();

                builder.RegisterType<AnyProcessWatch>().AsSelf();
                builder.RegisterType<PatternProcessWatch>().AsSelf();
            }
        }
    }
}
