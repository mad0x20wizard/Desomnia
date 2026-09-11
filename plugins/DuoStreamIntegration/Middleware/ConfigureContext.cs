using Autofac;
using Autofac.Core.Resolving.Pipeline;
using MadWizard.Desomnia.Service.Duo;
using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Service.Duo.Manager;

namespace MadWizard.Desomnia.Network.Middleware
{
    public sealed class ConfigureContext(DuoSessionMonitorConfig config) : IResolveMiddleware
    {
        public PipelinePhase Phase => PipelinePhase.ParameterSelection;

        public void Execute(ResolveRequestContext ctx, Action<ResolveRequestContext> next)
        {
            IEnumerable<DuoInstance> CreateInstances(IEnumerable<InstanceSettings> instances)
            {
                foreach (var settings in instances)
                {
                    string name = settings.Name;

                    var info = config[name] ?? new DuoInstanceWatchInfo { Name = name };
                    info.OnDemand ??= config.OnInstanceDemand;
                    info.OnIdle ??= config.OnInstanceIdle;
                    info.OnLogin ??= config.OnInstanceLogin;
                    info.OnStart ??= config.OnInstanceStarted;
                    info.OnStop ??= config.OnInstanceStopped;
                    info.OnLogout ??= config.OnInstanceLogout;

                    info.WatchStreamTraffic ??= config.WatchStreamTraffic;

                    yield return ctx.Resolve<DuoInstance>(
                        TypedParameter.From(name),
                        TypedParameter.From(info),
                        TypedParameter.From(settings)
                    );
                }
            }

            if (ctx.FirstParameterOfType<DuoSettings>() is DuoSettings settings)
            {
                var client = new HttpClient { BaseAddress = new Uri("http://localhost:" + settings.Port) };

                DuoManager manager = ctx.Resolve<DuoWebAPIManager>(TypedParameter.From(client));

                IEnumerable<DuoInstance> instances = [.. CreateInstances(settings.Instances)];

                ctx.ChangeParameters([ ..ctx.Parameters,
                    TypedParameter.From(manager),
                    TypedParameter.From(instances)
                ]);
                
                next(ctx);
            }
        }
    }
}
