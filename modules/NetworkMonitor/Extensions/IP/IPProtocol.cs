namespace System.Net
{
    [Flags]
    public enum IPProtocol
    {
        Unknown = 0,

        TCP = 1,
        UDP = 2,
    }

    public static class IPProcotolExt
    {
        extension(IPProtocol protocol)
        {
            public bool IsSingle => protocol is IPProtocol.TCP or IPProtocol.UDP;

            public bool IsMultiple => protocol == (IPProtocol.TCP | IPProtocol.UDP);
        }
    }
}
