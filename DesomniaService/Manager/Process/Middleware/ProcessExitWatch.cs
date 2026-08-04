using Autofac.Core.Resolving.Pipeline;
using MadWizard.Desomnia.Processes.Manager;

namespace MadWizard.Desomnia.Processes.Middleware
{
    /**
     * What used to be the tail of the manager's CreateProcess: a freshly created process starts
     * watching for its own exit. On the registration pipeline, so it sees the platform's concrete
     * process before any decoration wraps it.
     */
    public sealed class ProcessExitWatch : IResolveMiddleware
    {
        public PipelinePhase Phase => PipelinePhase.Activation;

        public void Execute(ResolveRequestContext context, Action<ResolveRequestContext> next)
        {
            next(context);

            if (context.Instance is Win32Process process)
            {
                process.WatchForExit();
            }
        }
    }
}
