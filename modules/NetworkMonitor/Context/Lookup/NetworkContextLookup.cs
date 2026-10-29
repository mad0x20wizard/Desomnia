using MadWizard.Desomnia.Network.Neighborhood;
using Nito.Disposables.Internals;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Context
{
    internal static class NetworkContextLookup
    {
        extension (NetworkContext context)
        {
            internal NetworkHostContext? FindHostContextBy(string? name)                    => context.FirstOrDefault(ctx => ctx.Host.Name.Equals(name));
            internal NetworkHostContext? FindHostContextBy(PhysicalAddress? physical)       => context.FirstOrDefault(ctx => physical?.Equals(ctx.Host.PhysicalAddress) ?? false);

            internal NetworkHost? FindHostBy(string? name)                                  => context.FindHostContextBy(name)?.Host;

            internal IEnumerable<NetworkHost> FindHostsByName(IEnumerable<string?> names)   => names.Select(context.FindHostBy).WhereNotNull();
        }

        extension (NetworkHostContext context)
        {
            internal NetworkServiceContext? FindServiceContextBy(IPService service) => context.FirstOrDefault(ctx => ctx.Service == service);
        }
    }
}
