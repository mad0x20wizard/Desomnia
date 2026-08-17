using Autofac;
using Autofac.Core;
using Autofac.Core.Registration;
using Autofac.Core.Resolving.Pipeline;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia
{
    class LoggingModule : Autofac.Module
    {
        protected override void AttachToComponentRegistration(IComponentRegistryBuilder registry, IComponentRegistration registration)
        {
            registration.PipelineBuilding += (sender, pipeline) =>
            {
                pipeline.Use(PipelinePhase.ParameterSelection, MiddlewareInsertionMode.StartOfPhase, (context, next) =>
                {
                    context.ChangeParameters(context.Parameters.Union(
                    [
                        new ResolvedParameter(
                            (pi, ctx) => pi.ParameterType == typeof(ILogger),

                            (pi, ctx) => ctx.Resolve<ILoggerFactory>().CreateLogger(registration.Activator.LimitType))
                    ]));

                    next(context);
                });
            };
        }
    }
}
