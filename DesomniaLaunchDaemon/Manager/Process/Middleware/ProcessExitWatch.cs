using Autofac;
using Autofac.Core.Resolving.Pipeline;

namespace MadWizard.Desomnia.Processes.Manager.Middleware
{
    /**
     * What used to be the tail of the manager's CreateProcess: a freshly created process is
     * handed to the kqueue watcher, which notices its exit between polls. On the registration
     * pipeline, so it sees the platform's concrete process before any decoration wraps it.
     *
     * The manager is resolved back from the container, which is safe now that the roster fills
     * on first use: no process is created inside the manager's own activation anymore, so there
     * is no self-construction guard left to trip.
     */
    public sealed class ProcessExitWatch : IResolveMiddleware
    {
        public PipelinePhase Phase => PipelinePhase.Activation;

        public void Execute(ResolveRequestContext context, Action<ResolveRequestContext> next)
        {
            next(context);

            if (context.Instance is LibProcProcess process)
            {
                context.Resolve<LibProcProcessManager>().WatchForExit(process);
            }
        }
    }
}
