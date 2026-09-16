using Autofac;
using MadWizard.Desomnia.Display.Manager;
using MadWizard.Desomnia.LaunchDaemon.Configuration;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.Power.Manager;
using MadWizard.Desomnia.Power.Source;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;
using MadWizard.Desomnia.Processes.Manager.Metrics;
using MadWizard.Desomnia.Processes.Manager.Middleware;
using Microsoft.Extensions.Configuration;

namespace MadWizard.Desomnia.LaunchDaemon
{
    internal class PlatformModule : Desomnia.ConfigurableModule
    {
        private GraphicsMeasurementMethod _graphicsMeasurement;

        protected override void LoadOnce(ContainerBuilder builder, IConfiguration configuration)
        {
            var config = Bind<LaunchDaemonConfig>(configuration);

            RegisterProcessManager(builder, config);

            // the display manager lives in the persistent container, so it survives a
            // configuration rebuild with its soft-disconnect holds and CG display ids intact;
            // created only on first demand, and recorded so a config-less rebuild can re-attach
            builder.RegisterType<MacOSDisplayManager>()
                .As<IDisplayManager>()
                .SingleInstance()
                .AsSelf();

            // the interface manager is persistent for the same reason: a standing disable
            // intent (and what it took away) must survive a configuration rebuild, and only
            // an instance that outlives every rebuild can restore it on process exit
            builder.RegisterType<NetToolsInterfaceManager>()
                .As<INetworkInterfaceManager>()
                .SingleInstance()
                .AsSelf();

            // Implementing Platform-Managers. The power manager is persistent (its IOKit assertions
            // and sleep/wake registration must outlive a configuration rebuild), which lets it serve
            // as the IPowerSourceProbe backing the "power" condition of every rebuild as well —
            // both notification sources on its one run loop.
            builder.RegisterType<IOKitPowerManager>()
                .As<IPowerManager>().As<IPowerSource>()
                .AsImplementedInterfaces()
                .SingleInstance();
        }

        private void RegisterProcessManager(ContainerBuilder builder, LaunchDaemonConfig config)
        {
            _graphicsMeasurement = SelectGraphicsMeasurement(config.ProcessManager.MeasureGPU);

            // Takes the place of the module's own polling fallback (its registration steps aside
            // for any IProcessManager already registered, and platform modules load first): same
            // polling, but a poll that finds nothing new costs a single syscall here.
            builder.RegisterType<LibProcProcessManager>()
                .WithParameter(TypedParameter.From(config.ProcessManager.PollInterval))
                .WithParameter(TypedParameter.From(config.ProcessManager.MeasureGPU))
                .WithParameter(TypedParameter.From(_graphicsMeasurement))
                .AsImplementedInterfaces()
                .As<IProcessMetricSupport>()
                .As<ProcessManager>()
                .SingleInstance()
                .AsSelf();

            // Externally owned, because the container must not track what it did not get to
            // keep: the manager holds every process' lifetime, and the persistent container
            // would otherwise remember a disposal reference for each of the hundreds a startup
            // materialises. The exit watch rides the registration pipeline, where the concrete
            // process is still unwrapped (the process module hooks its parent resolver onto
            // the same pipeline, for every platform's registration at once).
            builder.RegisterType<LibProcProcess>().As<IProcess>()
                .ConfigurePipeline(pipeline => pipeline.Use(new ProcessExitWatch()))
                .ExternallyOwned();

            // The decorator is the selected measurement method. No selector remains in the read
            // path, and a machine on which neither native method probes successfully gets no GPU
            // decorator at all.
            switch (_graphicsMeasurement)
            {
                case GraphicsMeasurementMethod.Process:
                    builder.RegisterDecorator<AGXGraphicsProcess, IProcess>();
                    break;

                case GraphicsMeasurementMethod.Coalition:
                    builder.RegisterDecorator<CoalitionGraphicsProcess, IProcess>();
                    break;
            }

            // The traffic meter, in place for every process and idle until one is asked about its
            // network: the decoration subscribes itself on the first NetworkData sample, and the
            // meter's socket exists only while subscribed accounts do. Registered together with the
            // decoration on purpose – a platform with no meter registers neither, and the metric is
            // then refused by IProcessMetricSupport rather than resolving into nothing.
            builder.RegisterDecorator<ProcessTrafficAccount, IProcess>();

            builder.RegisterType<NtStatTrafficListener>()
                .As<IProcessMetricSupport>()
                .As<IProcessTrafficMeter>()
                .SingleInstance()
                .AsSelf();
        }

        private static GraphicsMeasurementMethod SelectGraphicsMeasurement(GraphicsMeasurementMode configured)
        {
            return configured switch
            {
                GraphicsMeasurementMode.Process =>
                    Probe(AGXGraphicsProcess.Probe) ? GraphicsMeasurementMethod.Process : GraphicsMeasurementMethod.None,

                GraphicsMeasurementMode.Coalition =>
                    Probe(CoalitionGraphicsProcess.Probe) ? GraphicsMeasurementMethod.Coalition : GraphicsMeasurementMethod.None,

                GraphicsMeasurementMode.Automatic when Probe(AGXGraphicsProcess.Probe) =>
                    GraphicsMeasurementMethod.Process,

                GraphicsMeasurementMode.Automatic when Probe(CoalitionGraphicsProcess.Probe) =>
                    GraphicsMeasurementMethod.Coalition,

                GraphicsMeasurementMode.Automatic => GraphicsMeasurementMethod.None,

                _ => throw new ArgumentOutOfRangeException(nameof(configured), configured, "Unknown GPU measurement mode"),
            };
        }

        private static bool Probe(Func<bool> query)
        {
            try
            {
                return query();
            }
            catch (Exception exception) when (exception is
                DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or TypeInitializationException)
            {
                return false;
            }
        }

        protected override void Load(ContainerBuilder builder)
        {
            // This bridge belongs to the rebuilt application scope because its inspector does.
            // The persistent process decorators keep only thread-local snapshots opened by it.
            switch (_graphicsMeasurement)
            {
                case GraphicsMeasurementMethod.Process:
                    builder.RegisterType<AGXGraphicsInspectionBridge>().As<IStartable>().SingleInstance();
                    break;

                case GraphicsMeasurementMethod.Coalition:
                    builder.RegisterType<CoalitionGraphicsInspectionBridge>().As<IStartable>().SingleInstance();
                    break;
            }

            // Implementing Network-Managers
            builder.RegisterType<AddressResolutionCache>()
                .AsImplementedInterfaces()
                .InstancePerNetwork();
        }
    }
}
