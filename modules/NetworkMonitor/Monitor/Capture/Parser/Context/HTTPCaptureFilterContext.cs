using MadWizard.Desomnia.Network.Filter;
using Microsoft.Extensions.Primitives;

namespace MadWizard.Desomnia.Network.Capture
{
    internal sealed class HTTPCaptureFilterContext : ServiceCaptureFilterContext, IHTTPFilterContext
    {
        private readonly IIPServicePayloadFilterContext _payload;

        public int PayloadLength => _payload.PayloadLength;
        public byte[] PayloadData => _payload.PayloadData;

        public required string Method   { get; init; }
        public required string Path     { get; init; }

        public string QueryString       { get; init; } = string.Empty;

        public string? Version          { get; init; }
        public string? Scheme           { get; init; }
        public string? Authority        { get; init; }
        public string? Host             { get; init; }
        public ushort? Port             { get; init; }

        public IReadOnlyDictionary<string, StringValues> Query      { get; init; }
        public IReadOnlyDictionary<string, StringValues> Headers    { get; init; }
        public IReadOnlyDictionary<string, StringValues> Cookies    { get; init; }

        public HTTPCaptureFilterContext(IIPServicePayloadFilterContext payload) : base(payload.RemoteEndPoint, payload.Service)
        {
            _payload = payload;
        }
    }
}
