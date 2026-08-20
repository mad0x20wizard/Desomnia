using Microsoft.Extensions.Hosting;

namespace MadWizard.Desomnia.Application.Lifetime
{
    /// <summary>
    /// A no-op <see cref="IHostLifetime"/> for the inner application host. The process
    /// lifetime (Ctrl+C, SIGTERM, the Windows SCM) belongs to the OUTER host alone; the
    /// inner host is rebuilt on every configuration change and must never touch it — its
    /// shutdown is driven purely by the token passed to <see cref="HostingAbstractionsHostExtensions.RunAsync"/>
    /// (the linked restart/stop token from the application loop). Mirrors the BCL's own
    /// internal NullLifetime, which is not public.
    /// </summary>
    internal sealed class NullLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
