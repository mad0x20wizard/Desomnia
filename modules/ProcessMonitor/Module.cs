using Autofac;
using Autofac.Core.Resolving.Pipeline;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Configuration.Migration;
using MadWizard.Desomnia.Configuration.Xml;
using MadWizard.Desomnia.Processes.Manager;
using MadWizard.Desomnia.Processes.Middleware;
using MadWizard.Desomnia.Processes.Watch;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Processes
{
    public class Module : Desomnia.ConfigurableModule<ModuleConfig>, IXConfigurationMigration
    {
        #region Versioning
        protected override uint MinVersion => 2;

        void IXConfigurationMigration.Run(XDocument configuration, uint version)
        {
            switch (version)
            {
                case 2: V2.Run(configuration); break;
            }
        }
        #endregion

        /**
         * Every platform's process registration gets the parent introduction, wherever that
         * registration was made – the platforms register their concrete processes, and none of
         * them should have to remember this. LoadOnce runs eagerly on the shared persistent
         * builder while every registration is still a deferred callback, so the hook is in
         * place before any of them lands, whichever module loaded first.
         *
         * StartOfPhase, deliberately: the platform's exit watch sits on the same Activation
         * phase, and placed outermost the resolver walks the ancestry only after the watch
         * armed the process against its own exit.
         */
        protected override void LoadOnce(ContainerBuilder builder)
        {
            // What this assembly's own fallback process answers for, and nothing about anybody's
            // operating system: a platform states its counters in its own project and registers
            // them first, so this steps aside wherever one did.
            builder.RegisterType<DefaultProcessMetricSupport>()
                .As<IProcessMetricSupport>()
                .SingleInstance();

            builder.RegisterComposite<ProcessMetricSupportCollector, IProcessMetricSupport>();

            builder.ComponentRegistryBuilder.Registered += (sender, args) =>
            {
                if (args.ComponentRegistration.IsLimitedTo<ProcessHandle>())
                    args.ComponentRegistration.PipelineBuilding += (_, pipeline) =>
                        pipeline.Use(new ParentProcessResolver(), MiddlewareInsertionMode.StartOfPhase);
            };
        }

        protected override void Load(ContainerBuilder builder, ModuleConfig config)
        {
            // Every watch is asked, as it is built, whether this machine keeps the counters its
            // thresholds name – whichever module registered it, and whichever scope it lives in.
            builder.ComponentRegistryBuilder.Registered += (sender, args) =>
            {
                if (args.ComponentRegistration.IsLimitedTo<ProcessWatch>())
                    args.ComponentRegistration.PipelineBuilding += (_, pipeline) =>
                        pipeline.Use(new ProcessMetricWatchBuilder());
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
