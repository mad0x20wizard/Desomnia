using Autofac;
using Autofac.Core.Resolving.Pipeline;
using MadWizard.Desomnia.Network.Neighborhood;

namespace MadWizard.Desomnia.Network.Middleware
{
    internal class DynamicNetworkServices : IResolveMiddleware
    {
        public PipelinePhase Phase => PipelinePhase.Sharing;

        public void Execute(ResolveRequestContext context, Action<ResolveRequestContext> next)
        {
            next(context);

            if (context.ResolveOptional<NetworkHostWatch>() is NetworkHostWatch watch)
            {
                context.Instance = ((IEnumerable<NetworkService>)context.Instance!).Union(watch.Select(sw => sw.Service));
            }
        }
    }
}
