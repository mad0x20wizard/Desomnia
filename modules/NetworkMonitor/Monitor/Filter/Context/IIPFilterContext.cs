using System.Net;

namespace MadWizard.Desomnia.Network.Filter
{
    public interface IIPFilterContext : IFilterContext;

    public interface IIPServiceFilterContext : IIPFilterContext
    {
        public IPProtocol   Protocol => Service.Protocol;

        public IPService    Service         { get; }

        public int          PayloadLength   { get; }
        public byte[]       PayloadData     { get; }
    }

    public interface IIPServicePayloadFilterContext : IIPFilterContext;

    public interface IICMPFilterContext : IIPFilterContext
    {
        ICMPType Type { get; }
    }

    public enum ICMPType
    {
        Unknown = 0,

        Echo
    }
}
