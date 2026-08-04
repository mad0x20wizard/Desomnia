using Autofac.Core.Resolving.Pipeline;

namespace MadWizard.Desomnia.Processes.Manager.Middleware
{
    /**
     * What used to be the tail of the manager's CreateProcess: a freshly created process is
     * handed to the epoll watcher, which notices its exit between polls. On the registration
     * pipeline, so it sees the platform's concrete process before any decoration wraps it.
     *
     * The manager arrives by assignment, not by resolution: the first processes are created
     * inside the manager's own activation (IStartable.Start refreshes the roster before the
     * singleton is published), where resolving it back would trip Autofac's self-construction
     * guard and kill the daemon at build. The platform module fills the slot from the manager's
     * OnActivated instead – which runs after Start, but that gap cannot matter: no watcher can
     * exist before the first listener subscribes, and the watcher's own catch-up covers the
     * roster the initial refresh built.
     */
    public sealed class ProcessExitWatch : IResolveMiddleware
    {
        internal ProcFSProcessManager? Manager { private get; set; }

        public PipelinePhase Phase => PipelinePhase.Activation;

        public void Execute(ResolveRequestContext context, Action<ResolveRequestContext> next)
        {
            next(context);

            if (context.Instance is LinuxProcess process)
            {
                Manager?.WatchForExit(process);
            }
        }
    }
}
