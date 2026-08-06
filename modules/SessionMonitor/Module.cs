using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Network.SleepProxy.Registration;
using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Session.Manager;
using MadWizard.Desomnia.Session.Middleware;

namespace MadWizard.Desomnia.Session
{
    public class Module : Desomnia.ConfigurableModule<ModuleConfig>
    {
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

                if (monitor.RegisterWithSleepProxy)
                {
                    // Add RDP port to SleepProxyRegistration
                    builder.ComponentRegistryBuilder.Registered += (sender, args) =>
                    {
                        if (args.ComponentRegistration.IsLimitedTo<SleepProxyRegistration>())
                            args.ComponentRegistration.PipelineBuilding += (_, pipeline) =>
                                pipeline.Use(new RDPSleepProxyRegistration());
                    };
                }
            }
        }
    }
}
