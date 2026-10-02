using Autofac;
using Autofac.Core.Resolving.Pipeline;

namespace MadWizard.Desomnia.Service.Duo.Middleware
{
    internal class InstanceEnumerator : IResolveMiddleware
    {
        public PipelinePhase Phase => PipelinePhase.ParameterSelection;

        public void Execute(ResolveRequestContext ctx, Action<ResolveRequestContext> next)
        {
            var monitor = ctx.Resolve<DuoSessionMonitor>();

            ctx.AddParameter(CreateEnumeratorWith(monitor));

            next(ctx);
        }

        static IEnumerable<DuoInstance> CreateEnumeratorWith(DuoSessionMonitor monitor)
        {
            if (monitor.Context is DuoServiceContext context)
            {
                foreach (var instance in context.Instances)
                {
                    yield return instance;
                }
            }
        }
    }
}
