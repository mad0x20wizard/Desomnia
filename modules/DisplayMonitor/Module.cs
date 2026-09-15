using Autofac;
using Autofac.Core;
using Autofac.Core.Registration;
using MadWizard.Desomnia.Display.Configuration;
using MadWizard.Desomnia.Display.Configuration.Migration;
using MadWizard.Desomnia.Display.Environments;
using MadWizard.Desomnia.Display.Manager;
using MadWizard.Desomnia.Environments;
using MadWizard.Desomnia.Configuration.Xml;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Display
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

        static bool HadDisplayMonitor { get; set; } = false;

        static bool HasDisplayManager(IComponentRegistryBuilder builder) => builder.IsRegistered(new TypedService(typeof(IDisplayManager)));

        protected override void LoadOnce(ContainerBuilder builder)
        {
            builder.RegisterType<LidCondition>()
                .Named<IEnvironmentCondition>("lid")
                .OnlyIf(HasDisplayManager);
        }

        protected override void Load(ContainerBuilder builder, ModuleConfig config)
        {
            if (HadDisplayMonitor |= config.DisplayMonitor is not null)
            {
                // registered even without a <DisplayMonitor> configuration: the monitor is the
                // desired-state assertor and must be able to re-attach to a manager a previous
                // configuration created, to sweep its stale intents (see DisplayMonitor)

                builder.RegisterType<DisplayMonitor>().OnlyIf(HasDisplayManager)
                    .WithParameter(new TypedParameter(typeof(DisplayMonitorConfig), config.DisplayMonitor))
                    .AsImplementedInterfaces()
                    .SingleInstance()
                    .AsSelf();
            }
        }
    }
}
