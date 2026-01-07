using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Network.Neighborhood;

namespace MadWizard.Desomnia.Network.Context.Parameters
{
    internal class NetworkServiceParameter(string name) : ResolvedParameter(
        (pi, ctx) => pi.ParameterType == typeof(NetworkService),
        (pi, ctx) => ResolveWith(ctx, name))
    {
        private static NetworkService ResolveWith(IComponentContext ctx, string name)
        {
            foreach (NetworkService service in ctx.Resolve<IEnumerable<NetworkService>>())
                if (service.Name == name)
                    return service;

            throw new KeyNotFoundException(name);
        }

        internal static NetworkServiceParameter FindByName(string name) => new(name);
    }
}
