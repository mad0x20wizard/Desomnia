using System.Net;

namespace MadWizard.Desomnia.Network.Traefik.Filter
{
    internal interface ITraefikRequestFilter
    {
        public bool ShouldFilter(HttpListenerRequest request);
    }

    internal class CompositeTraefikRequestFilter(IEnumerable<ITraefikRequestFilter> filters) : ITraefikRequestFilter
    {
        bool ITraefikRequestFilter.ShouldFilter(HttpListenerRequest request)
        {
            foreach (ITraefikRequestFilter filter in filters)
            {
                if (filter.ShouldFilter(request))
                {
                    return true;
                }
            }

            return false;
        }
    }

}
