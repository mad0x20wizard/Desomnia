using Microsoft.Extensions.Hosting;

namespace MadWizard.Desomnia
{
    /// <summary>
    /// Graceful teardown for a persistent service that is created lazily, on first demand.
    /// Implementing this interface is all it takes: when the persistent container activates
    /// an instance, resolve middleware hands it to the <see cref="Application.Lifetime.ShutdownCoordinator"/>,
    /// whose <see cref="IHostedService.StopAsync"/> runs the teardowns with hosted-service
    /// stop timing — after the application host has drained, before <c>ApplicationStopped</c>
    /// releases the process lifetime (on Windows: before the SCM is told the service stopped) —
    /// but without the eager instantiation and automatic startup a hosted service brings.
    /// An instance that was never activated is never stopped.
    /// </summary>
    public interface IAsyncStoppable
    {
        /// <summary>Called once, in reverse activation order, bounded by
        /// <c>HostOptions.ShutdownTimeout</c> through the token. <c>Dispose</c> (if any)
        /// still follows later, with the container teardown — implementations that keep a
        /// disposal backstop must make the two meet idempotently.</summary>
        Task StopAsync(CancellationToken cancellationToken);
    }
}
