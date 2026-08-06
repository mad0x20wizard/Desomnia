using Autofac.Core.Resolving.Pipeline;
using MadWizard.Desomnia.Session.Configuration;

namespace MadWizard.Desomnia.Session.Middleware
{
    public sealed class SessionWatchConfigurator(SessionMonitorConfig config) : IResolveMiddleware
    {
        public PipelinePhase Phase => PipelinePhase.Activation;

        public void Execute(ResolveRequestContext context, Action<ResolveRequestContext> next)
        {
            next(context);

            if (context.Instance is SessionWatch watch)
            {
                config.Configure(watch.Session, watch.ApplyConfiguration);
            }
        }
    }
}
