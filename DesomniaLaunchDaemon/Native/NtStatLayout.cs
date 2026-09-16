namespace MadWizard.Desomnia.LaunchDaemon.Native
{
    /**
     * Where the fields sit in one kernel's flow descriptor – the whole of what this interface's
     * version dependence amounts to, deliberately gathered in one table so that supporting another
     * macOS is adding a row rather than reading the parser.
     *
     * There is no version to ask for. The revision constant the header carries has been frozen at 9
     * since 2017, is never transmitted and is never checked, and it stayed frozen while the
     * descriptor changed four times – so the kernel's own statement of its layout is the length of
     * the messages it sends: an update is "the fixed part plus this provider's descriptor", making
     * <c>length - <see cref="NtStat.SRC_UPDATE_FIXED"/></c> the size the running kernel was built
     * with. That size is the key here, and an unknown one is refused rather than guessed at, which
     * is what turns a silent misreading into a startup that says what it did not recognise.
     *
     * Two eras share every offset a meter needs: the fields that were added between macOS 11 and 26
     * were all appended past the ones below (rx/tx_transfer_size and fuuid in 11, persona_id and uid
     * in 14), so only the total size moved. Older kernels are absent on purpose – macOS 10.15 and
     * earlier put pid and pname elsewhere, and a row nobody has verified against a running kernel
     * would be worse than the refusal.
     */
    public sealed record NtStatLayout(string Era, int Size, int Pid, int EffectivePid, int Name)
    {
        /// <summary>TCP_KERNEL, TCP_USERLAND and QUIC_USERLAND – the last is a typedef of the first.</summary>
        public static readonly NtStatLayout[] TcpShaped =
        [
            new("macOS 14 – 26", Size: 344, Pid: 116, EffectivePid: 120, Name: 196),
            new("macOS 11 – 13", Size: 336, Pid: 116, EffectivePid: 120, Name: 196)
        ];

        /// <summary>UDP_KERNEL and UDP_USERLAND, which order their fields differently from the TCP one.</summary>
        public static readonly NtStatLayout[] UdpShaped =
        [
            new("macOS 14 – 26", Size: 280, Pid: 128, EffectivePid: 196, Name: 132),
            new("macOS 11 – 13", Size: 272, Pid: 128, EffectivePid: 196, Name: 132)
        ];

        /// <summary>The layout of that size, or null for one this build has never been told about.</summary>
        public static NtStatLayout? For(uint provider, int size)
        {
            var known = NtStat.IsUdpShaped(provider) ? UdpShaped : TcpShaped;

            return Array.Find(known, layout => layout.Size == size);
        }

        /// <summary>What the refusal says: every size this build would have recognised.</summary>
        public static string Describe(uint provider)
        {
            var known = NtStat.IsUdpShaped(provider) ? UdpShaped : TcpShaped;

            return string.Join(", ", known.Select(layout => $"{layout.Size} ({layout.Era})"));
        }

        public override string ToString() => $"{Size} bytes, {Era}";
    }
}
