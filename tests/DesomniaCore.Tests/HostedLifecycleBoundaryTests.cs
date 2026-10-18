using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    /// <summary>
    /// Isolated probes of the Generic Host boundaries proposed for ApplicationHost.
    /// These hosts use the real persistent-container bridge, without network or OS services.
    /// </summary>
    public class HostedLifecycleBoundaryTests
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
        private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task StartAndShutdown_AwaitAllHooks_AcrossApplicationRebuilds(bool concurrent)
        {
            var builder = new ContainerBuilder();
            builder.RegisterType<PersistentObserver>().SingleInstance();
            using var root = builder.Build();
            var observer = root.Resolve<PersistentObserver>();

            for (int iteration = 0; iteration < 2; iteration++)
            {
                var first = new Probe();
                var second = new Probe();
                using var stopping = new CancellationTokenSource();
                using (var application = BuildHost(root, concurrent, first, second))
                {
                    Assert.Same(observer, application.Services.GetRequiredService<PersistentObserver>());
                    var lifetime = application.Services.GetRequiredService<IHostApplicationLifetime>();
                    var startup = application.StartAsync(stopping.Token);

                    await first.StartEntered.Task.WaitAsync(TestTimeout);
                    if (concurrent) await second.StartEntered.Task.WaitAsync(TestTimeout);
                    Assert.False(startup.IsCompleted);
                    Assert.False(lifetime.ApplicationStarted.IsCancellationRequested);

                    first.StartAllowed.SetResult();
                    await second.StartEntered.Task.WaitAsync(TestTimeout);
                    Assert.False(first.StartedEntered.Task.IsCompleted);
                    second.StartAllowed.SetResult();

                    await first.StartedEntered.Task.WaitAsync(TestTimeout);
                    Assert.False(startup.IsCompleted);
                    Assert.False(lifetime.ApplicationStarted.IsCancellationRequested);
                    first.StartedAllowed.SetResult();
                    await second.StartedEntered.Task.WaitAsync(TestTimeout);
                    second.StartedAllowed.SetResult();
                    await startup.WaitAsync(TestTimeout);
                    Assert.True(lifetime.ApplicationStarted.IsCancellationRequested);
                    observer.Started++;

                    var shutdown = application.WaitForShutdownAsync(stopping.Token);
                    stopping.Cancel();
                    await second.StopEntered.Task.WaitAsync(TestTimeout);
                    Assert.False(shutdown.IsCompleted);
                    Assert.False(lifetime.ApplicationStopped.IsCancellationRequested);
                    second.StopAllowed.SetResult();
                    await first.StopEntered.Task.WaitAsync(TestTimeout);
                    first.StopAllowed.SetResult();
                    await shutdown.WaitAsync(TestTimeout);
                    Assert.True(lifetime.ApplicationStopped.IsCancellationRequested);
                    Assert.Equal(1, first.StopCalls);
                    Assert.Equal(1, second.StopCalls);
                    Assert.False(observer.Disposed);
                }
                observer.Stopped++;
                Assert.False(observer.Disposed);
            }

            Assert.Equal(2, observer.Started);
            Assert.Equal(2, observer.Stopped);
            root.Dispose();
            Assert.True(observer.Disposed);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task FailedOrCancelledStartup_DoesNotSignalApplicationStarted(bool cancel)
        {
            using var root = new ContainerBuilder().Build();
            var probe = new Probe();
            using var stopping = new CancellationTokenSource();
            using var application = BuildHost(root, false, probe);
            var lifetime = application.Services.GetRequiredService<IHostApplicationLifetime>();
            var startup = application.StartAsync(stopping.Token);
            await probe.StartEntered.Task.WaitAsync(TestTimeout);

            if (cancel)
            {
                stopping.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startup.WaitAsync(TestTimeout));
            }
            else
            {
                probe.StartAllowed.SetException(new InvalidOperationException("Startup failed"));
                await Assert.ThrowsAsync<InvalidOperationException>(() => startup.WaitAsync(TestTimeout));
            }
            Assert.False(lifetime.ApplicationStarted.IsCancellationRequested);
            Assert.False(probe.StartedEntered.Task.IsCompleted);

            // A failed StartAsync never reaches WaitForShutdownAsync. The caller must
            // explicitly stop the partially started host before disposing its container.
            probe.StopAllowed.SetResult();
            await application.StopAsync().WaitAsync(TestTimeout);
            Assert.Equal(1, probe.StopCalls);
        }

        private static IHost BuildHost(ILifetimeScope root, bool concurrent, params Probe[] probes)
        {
            return new HostBuilder()
                .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IHostLifetime, QuietLifetime>();
                    services.Configure<HostOptions>(options =>
                    {
                        options.ServicesStartConcurrently = concurrent;
                        options.ServicesStopConcurrently = concurrent;
                    });
                })
                .ConfigureContainer<ContainerBuilder>(builder =>
                {
                    builder.RegisterSource(new RootContainerBridge(root));
                    foreach (var probe in probes)
                        builder.RegisterInstance(probe).As<IHostedService>().ExternallyOwned();
                })
                .Build();
        }

        public sealed class PersistentObserver : IDisposable
        {
            public int Started;
            public int Stopped;
            public bool Disposed;
            public void Dispose() => Disposed = true;
        }

        private sealed class QuietLifetime : IHostLifetime
        {
            public Task WaitForStartAsync(CancellationToken token) => Task.CompletedTask;
            public Task StopAsync(CancellationToken token) => Task.CompletedTask;
        }

        private sealed class Probe : IHostedLifecycleService
        {
            public readonly TaskCompletionSource StartEntered = Signal();
            public readonly TaskCompletionSource StartAllowed = Signal();
            public readonly TaskCompletionSource StartedEntered = Signal();
            public readonly TaskCompletionSource StartedAllowed = Signal();
            public readonly TaskCompletionSource StopEntered = Signal();
            public readonly TaskCompletionSource StopAllowed = Signal();
            public int StopCalls;

            public Task StartingAsync(CancellationToken token) => Task.CompletedTask;
            public async Task StartAsync(CancellationToken token)
            {
                StartEntered.SetResult();
                await StartAllowed.Task.WaitAsync(token);
            }
            public async Task StartedAsync(CancellationToken token)
            {
                StartedEntered.SetResult();
                await StartedAllowed.Task.WaitAsync(token);
            }
            public Task StoppingAsync(CancellationToken token) => Task.CompletedTask;
            public async Task StopAsync(CancellationToken token)
            {
                StopCalls++;
                StopEntered.SetResult();
                await StopAllowed.Task.WaitAsync(token);
            }
            public Task StoppedAsync(CancellationToken token) => Task.CompletedTask;
        }
    }
}
