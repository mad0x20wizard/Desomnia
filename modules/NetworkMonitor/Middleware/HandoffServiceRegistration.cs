using Autofac;
using Autofac.Core.Resolving.Pipeline;
using MadWizard.Desomnia.Network.Handoff.Registration;
using MadWizard.Desomnia.Network.SleepProxy.Registration;
using MadWizard.Desomnia.Network.Watch;

namespace MadWizard.Desomnia.Network.Middleware
{
    public abstract class HandoffServiceRegistration : IResolveMiddleware
    {
        public PipelinePhase Phase => PipelinePhase.ParameterSelection;

        protected abstract IEnumerable<HandoffServiceInfo> HandoffServices(ResolveRequestContext context, LocalHostWatch watch);

        public void Execute(ResolveRequestContext context, Action<ResolveRequestContext> next)
        {
            next(context);

            if (context.FirstParameterOfType<LocalHostWatch>() is LocalHostWatch watch)
            {
                if (context.Instance is SleepProxyRegistration reg)
                {
                    foreach (var service in HandoffServices(context, watch))
                    {
                        if (!reg.Services.Any(s => s.IPPort == service.IPPort))
                        {
                            reg.Services.Add(service);
                        }
                    }
                }
            }
        }

    }
}
