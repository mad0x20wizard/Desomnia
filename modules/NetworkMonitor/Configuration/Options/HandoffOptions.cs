using MadWizard.Desomnia.Network.Neighborhood.Options;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Configuration.Options
{
    public readonly struct HandoffOptions
    {
        public readonly HandoffType Type            { get; init; }

        public readonly TimeSpan    Duration        { get; init; }

        public readonly TimeSpan    Timeout         { get; init; }
        public readonly int         Retry           { get; init; }

        /// <summary>
        /// Wire size a single registration message may grow to before it is split into a burst
        /// of update messages; null disables splitting, leaving oversized updates to IP fragmentation.
        /// </summary>
        public readonly ushort?     MTU             { get; init; }

        public readonly byte[]?     Password        { get; init; }

        public bool IsMandatory => Type.HasFlag(HandoffType.Mandatory);

        public static implicit operator IPAddressSelectionOptions(HandoffOptions options)
        {
            if (options.Type.HasFlag(HandoffType.IPv4) && !options.Type.HasFlag(HandoffType.IPv6))
                return new(AddressFamily.InterNetwork);
            if (options.Type.HasFlag(HandoffType.IPv6) && !options.Type.HasFlag(HandoffType.IPv4))
                return new(AddressFamily.InterNetworkV6);

            return new();
        }
    }

    [Flags]
    public enum HandoffType
    {
        None            = 0,

        SleepProxy      = 1 << 1,
        UnMagicPacket   = 1 << 2,
        Pattern         = 1 << 3,

        Mandatory       = 1 << 10,

        // IP protocol preference

        IPv4            = 1 << 15,
        IPv6            = 1 << 16,

        // Transport protocol features

        TCPKeepAlive    = 1 << 20 // TODO: for future improvement with Apple SPS
    }
}
