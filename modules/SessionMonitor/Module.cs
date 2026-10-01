using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Configuration.Xml;
using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Session.Configuration.Migration;
using MadWizard.Desomnia.Session.Manager;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Session
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

        protected override void Load(ContainerBuilder builder, ModuleConfig config)
        {
            if (config.SessionMonitor is SessionMonitorConfig monitor)
            {
                builder.RegisterType<SessionMonitor>()
                    .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(ISessionManager))))
                    .WithParameter(new TypedParameter(typeof(SessionMonitorConfig), monitor))
                    .AsImplementedInterfaces()
                    .SingleInstance()
                    .AsSelf();
            }
        }
    }
}
