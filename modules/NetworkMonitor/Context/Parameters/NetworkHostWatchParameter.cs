using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Network.Neighborhood;

namespace MadWizard.Desomnia.Network.Context.Parameters
{
    internal class NetworkHostWatchParameter<T>(string name) : ResolvedParameter(
        (pi, ctx) => pi.ParameterType == typeof(T),
        (pi, ctx) => ResolveWith(ctx, name)) where T : NetworkHostWatch
    {
        private static NetworkHostWatch ResolveWith(IComponentContext ctx, string name)
        {
            if (ctx.Resolve<NetworkSegment>()[name] is NetworkHost host)
                if (ctx.Resolve<NetworkMonitor>()[host] is NetworkHostWatch watch)
                    return watch;

            throw new KeyNotFoundException(name);
        }

        internal static NetworkHostWatchParameter<T> FindByHostName(string name) => new(name);
    }
}
