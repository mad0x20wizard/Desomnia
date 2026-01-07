using Autofac;
using Autofac.Core.Resolving.Pipeline;

namespace MadWizard.Desomnia.Network.Middleware
{
    internal class DynamicNetworkMonitors : IResolveMiddleware
    {
        public PipelinePhase Phase => PipelinePhase.Sharing;

        public void Execute(ResolveRequestContext context, Action<ResolveRequestContext> next)
        {
            next(context);

            var dno = context.Resolve<DynamicNetworkObserver>();

            context.Instance = ((IEnumerable<IInspectable>)context.Instance!).Union(dno);
        }
    }
}
