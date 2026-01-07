using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Network.Neighborhood;

namespace MadWizard.Desomnia.Network.Context.Parameters
{
    internal class NetworkHostsParameter(params string[] names) : ResolvedParameter(
        (pi, ctx) => pi.ParameterType == typeof(IEnumerable<NetworkHost>),
        (pi, ctx) => ResolveWith(ctx, names))
    {
        private static IEnumerable<NetworkHost> ResolveWith(IComponentContext ctx, IEnumerable<string> names)
        {
            List<NetworkHost> hosts = [];

            foreach (var name in names)
                if (ctx.Resolve<NetworkSegment>()[name] is NetworkHost host)
                    hosts.Add(host);
                else
                    throw new KeyNotFoundException(name);

            return hosts;
        }

        internal static NetworkHostsParameter FindBy(params string[] names) => new(names);
    }
}
