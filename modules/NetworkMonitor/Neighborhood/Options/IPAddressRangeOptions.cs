namespace MadWizard.Desomnia.Network.Neighborhood.Options
{
    public struct IPAddressRangeOptions
    {
        public IPAddressRangeFlags Flags { get; set; }

        public DateTime? Expires { get; set; }

        public IPAddressRangeOptions(IPAddressRangeFlags flags)
        {
            Flags = flags;
        }

        public IPAddressRangeOptions(TimeSpan? lifetime)
        {
            if (lifetime.HasValue)
            {
                Expires = DateTime.Now + lifetime;
            }
        }

        public readonly bool HasFlags(IPAddressRangeFlags flags) => Flags.HasFlag(flags);

        public readonly bool HasExpired => Expires < DateTime.Now;
    }

    public enum IPAddressRangeFlags
    {
        None        = 0,

        Static      = 1 << 0,
    }
}
