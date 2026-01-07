using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Network.Neighborhood;

namespace MadWizard.Desomnia.Network.Context.Parameters
{
    internal class LocalHostParameter<T>() : ResolvedParameter(
        (pi, ctx) => pi.ParameterType == typeof(T),
        (pi, ctx) => ResolveWith(ctx)) where T : NetworkHost
    {
        private static NetworkHost ResolveWith(IComponentContext ctx)
        {
            if (ctx.Resolve<NetworkSegment>().OfType<LocalHost>().FirstOrDefault() is LocalHost host)
                return host;

            throw new KeyNotFoundException();
        }

        internal static LocalHostParameter<T> FindBy() => new();
    }
}
