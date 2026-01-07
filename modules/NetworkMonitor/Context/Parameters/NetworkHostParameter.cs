using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Network.Neighborhood;

namespace MadWizard.Desomnia.Network.Context.Parameters
{
    internal class NetworkHostParameter<T>(string name) : ResolvedParameter(
        (pi, ctx) => pi.ParameterType == typeof(T),
        (pi, ctx) => ResolveWith(ctx, name)) where T : NetworkHost
    {
        private static NetworkHost ResolveWith(IComponentContext ctx, string name)
        {
            if (ctx.Resolve<NetworkSegment>()[name] is T host)
                return host;

            throw new KeyNotFoundException(name);
        }

        internal static NetworkHostParameter<T> FindBy(string name) => new(name);
    }
}
