using Autofac;
using MadWizard.Desomnia.Application;
using MadWizard.Desomnia.Application.Shutdown;
using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Environments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    /// <summary>
    /// The custom host: the rebuild loop lives outside the container, and fatal
    /// configuration errors escape <see cref="ApplicationHost.Run"/> back to the process entry
    /// point with a non-zero exit code — the self-heal contract with the service managers.
    /// </summary>
    public class DesomniaHostTests : IDisposable
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

        private readonly string _configPath = Path.Combine(Path.GetTempPath(), $"desomnia-host-{Guid.NewGuid():N}.xml");

        public void Dispose()
        {
            File.Delete(_configPath);

            Environment.ExitCode = 0; // never leak the fatal-exit marker into other tests
        }

        public sealed class StrictConfig
        {
            public TimeSpan? Timeout { get; set; }
        }

        private sealed class StrictModule : ConfigurableModule<StrictConfig>
        {
            protected override void Load(ContainerBuilder builder, StrictConfig config) { }
        }

        [Fact]
        public async Task InvalidFirstBuild_EscapesRun_WithExitCode()
        {
            File.WriteAllText(_configPath, """<SystemMonitor timeout="banana" />""");

            var builder = new ApplicationBuilder(_configPath);
            builder.RegisterModule(new StrictModule());

            var host = builder.Build();

            // the strict binder rejects "banana" during the first inner build; the loop
            // records the fatal, stops everything, and Run rethrows it to the entry point
            await Assert.ThrowsAsync<ConfigurationValueException>(
                () => host.RunAsync().WaitAsync(TestTimeout));

            Assert.Equal(1, Environment.ExitCode);
        }

        private sealed class SelfStoppingModule : Module
        {
            protected override void Load(ContainerBuilder builder)
                => builder.RegisterType<SelfStoppingService>().As<IHostedService>().SingleInstance();
        }

        private sealed class SelfStoppingService(IHostApplicationLifetime lifetime) : IHostedService
        {
            public Task StartAsync(CancellationToken cancellationToken)
            {
                // a deliberate inner stop (the promiscuous-mode-mutex pattern): no reload is
                // pending, so the loop must end the whole process - normally
                lifetime.StopApplication();

                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        [Fact]
        public async Task DeliberateInnerStop_EndsTheRunCleanly()
        {
            File.WriteAllText(_configPath, """<SystemMonitor />""");

            var builder = new ApplicationBuilder(_configPath);
            builder.RegisterModule(new SelfStoppingModule());

            var host = builder.Build();

            await host.RunAsync().WaitAsync(TestTimeout); // completes without throwing

            Assert.Equal(0, Environment.ExitCode);
        }

        private sealed class StartedSignalModule(TaskCompletionSource started) : Module
        {
            protected override void Load(ContainerBuilder builder)
                => builder.Register(_ => new StartedSignalService(started)).As<IHostedService>().SingleInstance();
        }

        private sealed class StartedSignalService(TaskCompletionSource started) : IHostedService
        {
            public Task StartAsync(CancellationToken cancellationToken)
            {
                started.TrySetResult();

                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        [Fact]
        public async Task FatalEditAtRuntime_EscapesRun_WithExitCode()
        {
            File.WriteAllText(_configPath, """<SystemMonitor />""");

            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var builder = new ApplicationBuilder(_configPath);
            builder.RegisterModule(new StartedSignalModule(started));

            var host = builder.Build();

            var run = host.RunAsync();

            await started.Task.WaitAsync(TestTimeout); // the inner host is up

            // an edit the process cannot apply: the root element switches modes
            File.WriteAllText(_configPath, """
                <EnvironmentMonitor>
                  <Environment test="x"><SystemMonitor /></Environment>
                </EnvironmentMonitor>
                """);

            host.Services.GetRequiredService<ConfigurationPipeline>().CheckForChanges();

            await Assert.ThrowsAsync<ConfigurationValueException>(() => run.WaitAsync(TestTimeout));

            Assert.Equal(1, Environment.ExitCode);
        }
    }
}
