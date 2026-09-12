using Autofac;
using Autofac.Features.OwnedInstances;
using MadWizard.Desomnia.Service.Duo;
using MadWizard.Desomnia.Service.Duo.Sunshine;
using MadWizard.Desomnia.Service.Duo.Sunshine.Listener;
using Microsoft.Extensions.Logging.Abstractions;
using System.ServiceProcess;
using Xunit;
using static DuoStreamIntegration.Tests.DuoTestSupport;

namespace DuoStreamIntegration.Tests;

public sealed class SunshineListenerAdapterTests
{
    [Theory, Trait("Issue", "6")]
    [InlineData(true)]
    [InlineData(false)]
    public void Build_callback_starts_monitor_after_adapter_attaches_when_service_is_initially_running_or_stopped(bool initiallyRunning)
    {
        using var instance = Instance();
        var manager = new ControlledManager();
        var context = Context(manager, Watcher(manager), instance);
        var service = new FakeDuoService
        {
            ObservedStatus = initiallyRunning ? ServiceControllerStatus.Running : ServiceControllerStatus.Stopped
        };
        var created = new List<TestListener>();
        var startupOrder = new List<string>();
        var builder = new ContainerBuilder();
        // Use the production startup mechanisms with controlled Windows dependencies.
        builder.Register(_ =>
        {
            var monitor = Monitor(service, _ => new Owned<DuoServiceContext>(context, context));
            monitor.TrackingStarted += (_, _) => startupOrder.Add("Instance tracked");
            return monitor;
        }).AsSelf().SingleInstance();
        builder.Register(ctx => new SunshineListenerAdapter(ctx.Resolve<DuoSessionMonitor>())
        {
            Logger = NullLogger<SunshineListenerAdapter>.Instance,
            CreateSunshineListener = service =>
            {
                // Construction/disposal do not open sockets or configure the firewall.
                var listener = new TestListener(service) { Logger = NullLogger<SunshineListener>.Instance, Firewall = null! };
                created.Add(listener);
                return listener;
            }
        }).OnActivated(ctx =>
        {
            ctx.Instance.Attach();
            startupOrder.Add("Adapter attached");
        }).AutoActivate().SingleInstance();
        builder.RegisterBuildCallback(container => container.ResolveOptional<DuoSessionMonitor>()?.Startup());
        using var container = builder.Build();
        var monitor = container.Resolve<DuoSessionMonitor>();
        try
        {
            if (!initiallyRunning) service.Publish(ServiceControllerStatus.Running);

            Assert.Equal(new[] { "Adapter attached", "Instance tracked" }, startupOrder);
            Assert.True(created.Count == 1, $"Expected one listener. Startup sequence: {string.Join(" -> ", startupOrder)}");
            var listener = Assert.Single(created);
            Assert.Same(listener, Assert.Single(instance.OfType<SunshineListener>()));
            monitor.StartTracking(instance); // Duplicate tracking must not add a second listener.
            Assert.Single(created);
            monitor.StopTracking(instance);
            Assert.Empty(instance.OfType<SunshineListener>());
            Assert.True(listener.Disposed);
        }
        finally
        {
            monitor.Dispose();
            foreach (var listener in created) listener.Dispose();
        }
    }

    private sealed class TestListener(SunshineService service) : SunshineListener(service)
    {
        public bool Disposed { get; private set; }
        public override void Dispose() { Disposed = true; base.Dispose(); }
    }
}
