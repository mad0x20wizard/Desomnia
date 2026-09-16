using Autofac;
using Autofac.Core.Resolving.Pipeline;
using MadWizard.Desomnia.Processes.Manager;

namespace MadWizard.Desomnia.Processes.Middleware
{
    /**
     * What used to be the tail of the manager's TriggerStart: a freshly created process is
     * introduced to its parent – found in the roster, or adopted through the same TriggerStart
     * that is creating the child. On the registration pipeline, so it sees every platform's
     * concrete process and acts on nothing but the description the manager handed the factory;
     * TriggerStart itself holds no opinion about ancestry anymore.
     *
     * Resolving the manager back from the container is safe here precisely because the roster
     * fills on first use, never inside the manager's own activation – the reason this could not
     * be a middleware before.
     */
    internal sealed class ParentProcessResolver : IResolveMiddleware
    {
        public PipelinePhase Phase => PipelinePhase.Activation;

        public void Execute(ResolveRequestContext context, Action<ResolveRequestContext> next)
        {
            next(context);

            if (context.Instance is ProcessHandle handle)
            {
                MaybeResolve(context.Resolve<ProcessManager>(), handle, context.FirstParameterOfType<ProcessInformation>());
            }
        }

        /**
         * The ancestry, walked one level at a time: a parent the platform already named is
         * trusted (an ETW start event, a stat line), an unknown one is asked of
         * QueryParentProcess – which is also where the pid-reuse verification lives. An adopted
         * ancestor climbs on through its own creation, one level of budget poorer, which is
         * what bounds a platform that reports a loop.
         */
        internal static void MaybeResolve(ProcessManager manager, ProcessHandle handle, in ProcessInformation info)
        {
            if (info.MaxParents > 0)
            {
                if ((info.ParentId ?? manager.QueryParentProcess(info)) is ProcessInformation parentInfo)
                {
                    if (parentInfo.Id == 0 || parentInfo.Id == info.Id)
                        return;

                    if (!manager.TryFindProcess(parentInfo.Id, out IProcess? parent))
                    {
                        parent = manager.TriggerStart(parentInfo with { MaxParents = info.MaxParents - 1 });
                    }

                    handle.Parent = parent;
                }
            }
        }
    }
}
