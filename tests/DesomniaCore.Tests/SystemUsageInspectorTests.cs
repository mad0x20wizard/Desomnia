using Autofac;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Power.Manager;
using MadWizard.Desomnia.Power.Source;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    public class SystemUsageInspectorTests
    {
        [Fact]
        public async Task InspectingAndInspected_BracketTheInspectionAttempt()
        {
            using var scope = new ContainerBuilder().Build();
            var system = new SystemMonitor(new SystemMonitorConfig(), new FakePowerManager())
            {
                Logger = NullLogger<SystemMonitor>.Instance,
                Scope = scope,
            };
            var inspector = new SystemUsageInspector(system)
            {
                Interval = TimeSpan.FromMilliseconds(1),
                Logger = NullLogger<SystemUsageInspector>.Instance,
            };
            var order = new List<string>();
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            inspector.Inspecting += (_, _) => order.Add("start");
            inspector.Inspected += (_, _) =>
            {
                order.Add("end");
                completed.TrySetResult();
            };

            await inspector.StartAsync(CancellationToken.None);

            try
            {
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
            finally
            {
                await inspector.StopAsync(CancellationToken.None);
                inspector.Dispose();
                system.Dispose();
            }

            Assert.Collection(order.Take(2),
                item => Assert.Equal("start", item),
                item => Assert.Equal("end", item));
        }

        private sealed class FakePowerManager : IPowerManager
        {
            public PowerSource Source => PowerSource.Unknown;

            public event EventHandler? Suspended { add { } remove { } }
            public event EventHandler? ResumeSuspended { add { } remove { } }

            public Task Suspend() => Task.CompletedTask;
            public Task Hibernate() => Task.CompletedTask;
            public Task Shutdown(TimeSpan? timeout = null, string? message = null, bool force = false) => Task.CompletedTask;
            public Task Reboot(TimeSpan? timeout = null, string? message = null, bool force = false) => Task.CompletedTask;
            public Task<IPowerRequest> CreateRequest(PowerRequestType type, string reason) => throw new NotSupportedException();

            public async IAsyncEnumerator<IPowerRequest> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                await Task.CompletedTask;
                yield break;
            }
        }
    }
}
