using Microsoft.Extensions.Primitives;

namespace MadWizard.Desomnia.Network.Filter
{
    public interface IHTTPFilterContext : IIPServicePayloadFilterContext
    {
        public string Method  { get; }
        public string Path    { get; }
        public string Version { get; }
        public string Host    { get; }

        public IReadOnlyDictionary<string, StringValues> Headers { get; }
        public IReadOnlyDictionary<string, string?> Cookies { get; }

        // TODO more?
    }
}
