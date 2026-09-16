using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace MadWizard.Desomnia.LaunchDaemon.Native
{
    /**
     * The private kernel control "com.apple.network.statistics" – the ledger nettop and Activity
     * Monitor read, and the only place on this platform where network bytes are counted per
     * process. proc_pid_rusage counts disk and not network, a pid's sockets report how full their
     * buffers are rather than what went through them, and a packet capture sees bytes without an
     * owner.
     *
     * Spoken directly rather than through NetworkStatistics.framework, which wraps this same
     * control: the framework would cost Objective-C blocks, a dispatch queue and CoreFoundation
     * marshalling, where the control itself is a socket, four syscalls and a struct. That keeps
     * the daemon's ahead-of-time build free of the things that are hardest to do without a
     * runtime, and it is the reason this file is plain P/Invoke.
     *
     * What is version-dependent about it lives in <see cref="NtStatLayout"/>, not here: the message
     * framing below has been unchanged since macOS 10.13, while the descriptors inside it have not.
     *
     * Probe-verified on macOS 15.5 (xnu-11417, arm64), privileged and unprivileged.
     */
    public static unsafe partial class NtStat
    {
        /// <summary>Not a file on disk since Big Sur — the dyld shared cache resolves it. Never preflight with File.Exists.</summary>
        const string LibSystem = "/usr/lib/libSystem.B.dylib";

        public const string ControlName = "com.apple.network.statistics";

        #region Message types and flags
        public const uint MSG_SUCCESS = 0;
        public const uint MSG_ERROR = 1;

        public const uint MSG_ADD_ALL_SRCS = 1002;
        public const uint MSG_GET_UPDATE = 1007;

        public const uint MSG_SRC_ADDED = 10001;
        public const uint MSG_SRC_REMOVED = 10002;
        public const uint MSG_SRC_COUNTS = 10004;
        public const uint MSG_SRC_UPDATE = 10006;

        /**
         * Set on every request, including the first: the kernel batches a poll into hundreds only
         * for a client that asked it to. Without the flag it enqueues every flow at once and stops
         * where the socket buffer ends – which arrives as ENOBUFS with no success behind it and an
         * answer silently missing whatever did not fit.
         */
        public const ushort FLAG_CONTINUATION = 1 << 1;

        /// <summary>On the last update of a flow that is going away, carrying its final counts.</summary>
        public const ushort FLAG_CLOSING = 1 << 2;

        /// <summary>On a removal whose final counts could not be delivered – the bytes since the last poll are lost.</summary>
        public const ushort FLAG_CLOSED_AFTER_DROP = 1 << 3;
        #endregion

        #region Providers
        public const uint PROVIDER_TCP_KERNEL = 2;
        public const uint PROVIDER_TCP_USERLAND = 3;
        public const uint PROVIDER_UDP_KERNEL = 4;
        public const uint PROVIDER_UDP_USERLAND = 5;
        public const uint PROVIDER_QUIC_USERLAND = 8;

        /**
         * All five, because two of them are not optional in the way they look: flows belonging to
         * Apple's userspace networking stack – which is most of what a modern application opens –
         * are registered by the flowswitch and surface as the USERLAND providers, never as the
         * kernel ones. Subscribing only to TCP_KERNEL and UDP_KERNEL would leave those processes
         * looking permanently idle.
         */
        public static readonly uint[] TrafficProviders =
            [PROVIDER_TCP_KERNEL, PROVIDER_TCP_USERLAND, PROVIDER_UDP_KERNEL, PROVIDER_UDP_USERLAND, PROVIDER_QUIC_USERLAND];

        public static bool IsUdpShaped(uint provider) => provider is PROVIDER_UDP_KERNEL or PROVIDER_UDP_USERLAND;

        public static string ProviderName(uint provider) => provider switch
        {
            PROVIDER_TCP_KERNEL => "TCP_KERNEL",
            PROVIDER_TCP_USERLAND => "TCP_USERLAND",
            PROVIDER_UDP_KERNEL => "UDP_KERNEL",
            PROVIDER_UDP_USERLAND => "UDP_USERLAND",
            PROVIDER_QUIC_USERLAND => "QUIC_USERLAND",
            _ => $"provider#{provider}"
        };
        #endregion

        #region Message framing — unchanged since macOS 10.13
        /// <summary>context (8), type (4), length (2), flags (2).</summary>
        public const int HDR_SIZE = 16;

        /// <summary>hdr, filter, events, provider, target pid, target uuid.</summary>
        public const int ADD_ALL_SRCS_SIZE = 56;

        /// <summary>hdr and a source reference – what QUERY_SRC, GET_SRC_DESC and GET_UPDATE all take.</summary>
        public const int SOURCE_REQUEST_SIZE = 24;

        /// <summary>hdr, srcref, event flags, counts, provider, reserved – the descriptor follows.</summary>
        public const int SRC_UPDATE_FIXED = 152;

        /// <summary>nstat_msg_src_counts in full: the same counts, without a descriptor behind them.</summary>
        public const int SRC_COUNTS_SIZE = 144;

        public const int UPDATE_COUNTS_OFFSET = 32;
        public const int UPDATE_PROVIDER_OFFSET = 144;

        // within nstat_counts
        public const int COUNTS_RXBYTES = 8;
        public const int COUNTS_TXBYTES = 24;

        /// <summary>Asks about every flow at once, which is the only way this is worth polling.</summary>
        public const ulong SRC_REF_ALL = ulong.MaxValue;

        /**
         * Nothing announced, everything reported.
         *
         * The word is an interface acceptance mask as well as a set of behaviour bits, and the
         * acceptance test runs only where at least one of its bits is set – so 0 asks for no
         * filtering, and setting the mask could only ever report less. The one bit set here stops
         * the kernel announcing every flow already open when a subscription is taken out, which is
         * a burst of no use to a meter that reads the counts anyway (probe-verified as suppressing
         * the announcements without hiding the flows behind them).
         */
        public const ulong FILTER_SUPPRESS_SRC_ADDED = 0x100000;
        #endregion

        #region Reading
        public static ulong ReadU64(ReadOnlySpan<byte> data, int offset)
            => offset + 8 <= data.Length ? BinaryPrimitives.ReadUInt64LittleEndian(data[offset..]) : 0;

        public static uint ReadU32(ReadOnlySpan<byte> data, int offset)
            => offset + 4 <= data.Length ? BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]) : 0;

        public static ushort ReadU16(ReadOnlySpan<byte> data, int offset)
            => offset + 2 <= data.Length ? BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]) : (ushort)0;

        public static int ReadI32(ReadOnlySpan<byte> data, int offset)
            => offset + 4 <= data.Length ? BinaryPrimitives.ReadInt32LittleEndian(data[offset..]) : 0;

        public static uint TypeOf(ReadOnlySpan<byte> message) => ReadU32(message, 8);
        public static ushort LengthOf(ReadOnlySpan<byte> message) => ReadU16(message, 12);
        public static ushort FlagsOf(ReadOnlySpan<byte> message) => ReadU16(message, 14);
        public static ulong ContextOf(ReadOnlySpan<byte> message) => ReadU64(message, 0);

        /// <summary>The flow this message is about – at the same place in every one that names one.</summary>
        public static ulong SourceRefOf(ReadOnlySpan<byte> message) => ReadU64(message, 16);

        /// <summary>Only meaningful for an error: the errno the kernel refused with.</summary>
        public static int ErrorOf(ReadOnlySpan<byte> message) => ReadI32(message, 16);
        #endregion

        #region Writing
        public static void WriteHeader(Span<byte> message, ulong context, uint type, ushort length, ushort flags = 0)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(message, context);
            BinaryPrimitives.WriteUInt32LittleEndian(message[8..], type);

            // exactly the datagram's length, never an approximation: the kernel silently rewrites a
            // wrong one, and takes the flags with it – which would drop the continuation bit above
            BinaryPrimitives.WriteUInt16LittleEndian(message[12..], length);
            BinaryPrimitives.WriteUInt16LittleEndian(message[14..], flags);
        }

        /// <summary>The subscription: this provider, this filter, for as long as the socket lives.</summary>
        public static byte[] AddAllSources(ulong context, uint provider, ulong filter)
        {
            var message = new byte[ADD_ALL_SRCS_SIZE];

            WriteHeader(message, context, MSG_ADD_ALL_SRCS, ADD_ALL_SRCS_SIZE);

            BinaryPrimitives.WriteUInt64LittleEndian(message.AsSpan(16), filter);
            BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(32), provider);

            return message;
        }

        public static byte[] SourceRequest(ulong context, uint type, ulong sourceRef, ushort flags)
        {
            var message = new byte[SOURCE_REQUEST_SIZE];

            WriteHeader(message, context, type, SOURCE_REQUEST_SIZE, flags);

            BinaryPrimitives.WriteUInt64LittleEndian(message.AsSpan(16), sourceRef);

            return message;
        }
        #endregion

        #region Control id
        /**
         * The control's numeric id, which every connection needs and only the kernel knows.
         *
         * Two ways to ask, because the documented one is an ioctl and ioctl is variadic: on Apple's
         * arm64 ABI a variadic argument travels on the stack and libsyscall's wrapper reads it from
         * there, while on x86_64 it rides a register like any other. A managed P/Invoke declares a
         * fixed signature and therefore has to pick, and picking wrong hands the kernel a pointer
         * from whatever the stack held – for a call that writes a hundred bytes back through it.
         * So the shape is chosen by architecture (arm64 verified on macOS 15.5), and where it
         * fails, the same id is read out of a sysctl that needs no ioctl at all.
         */
        public static uint? ResolveControlId(out string failure)
        {
            failure = "";

            int fd = socket(PF_SYSTEM, SOCK_DGRAM, SYSPROTO_CONTROL);

            if (fd < 0)
            {
                failure = $"socket(PF_SYSTEM): {ErrnoName(Errno)}";

                return null;
            }

            try
            {
                // struct ctl_info { u_int32_t ctl_id; char ctl_name[96]; }
                var info = new byte[4 + MAX_KCTL_NAME];

                Encoding.ASCII.GetBytes(ControlName).CopyTo(info, 4);

                fixed (byte* p = info)
                {
                    int result = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                        ? ioctl_stack(fd, CTLIOCGINFO, 0, 0, 0, 0, 0, 0, p)
                        : ioctl(fd, CTLIOCGINFO, p);

                    if (result == 0)
                        return ReadU32(info, 0);
                }

                failure = $"ioctl(CTLIOCGINFO): {ErrnoName(Errno)}";
            }
            finally
            {
                close(fd);
            }

            if (ResolveControlIdBySysctl() is uint fallback)
            {
                failure = "";

                return fallback;
            }

            return null;
        }

        /**
         * The same id, out of the kernel's list of registered controls: length-prefixed records
         * whose last 96 bytes are the name. Read off those two anchors rather than off the record
         * layout, so it survives the middle of the struct changing – which is the point of having
         * it at all.
         */
        private static uint? ResolveControlIdBySysctl()
        {
            if (Sysctl("net.systm.kctl.reg_list") is not byte[] raw)
                return null;

            var offset = 0;

            while (offset + 4 <= raw.Length)
            {
                int length = (int)ReadU32(raw, offset);

                if (length < 8 || offset + length > raw.Length)
                    break;

                if (length >= 4 + MAX_KCTL_NAME)
                {
                    var name = ReadName(raw.AsSpan(offset + length - MAX_KCTL_NAME, MAX_KCTL_NAME));

                    if (name == ControlName)
                        return ReadU32(raw, offset + 8);
                }

                offset += length;
            }

            return null;
        }

        /// <summary>A NUL-terminated name out of a fixed-width field.</summary>
        public static string ReadName(ReadOnlySpan<byte> field)
        {
            int end = field.IndexOf((byte)0);

            return Encoding.UTF8.GetString(end < 0 ? field : field[..end]);
        }

        private static byte[]? Sysctl(string name)
        {
            nuint length = 0;

            if (sysctlbyname(name, null, &length, null, 0) != 0 || length == 0)
                return null;

            var buffer = new byte[(int)length];

            fixed (byte* p = buffer)
            {
                if (sysctlbyname(name, p, &length, null, 0) != 0)
                    return null;
            }

            return buffer;
        }
        #endregion

        #region P/Invoke
        public const int PF_SYSTEM = 32;
        public const int AF_SYSTEM = 32;
        public const int SOCK_DGRAM = 2;
        public const int SYSPROTO_CONTROL = 2;
        public const int AF_SYS_CONTROL = 2;

        public const int MAX_KCTL_NAME = 96;

        /// <summary>_IOWR('N', 3, struct ctl_info), whose 100 bytes are the 0x64 in the middle.</summary>
        public const ulong CTLIOCGINFO = 0xC0644E03;

        public const int SOL_SOCKET = 0xFFFF;
        public const int SO_RCVBUF = 0x1002;
        public const int SO_RCVTIMEO = 0x1006;

        public const int EAGAIN = 35;
        public const int EINVAL = 22;
        public const int ENOBUFS = 55;

        public static int Errno => Marshal.GetLastWin32Error();

        public static string ErrnoName(int errno) => errno switch
        {
            0 => "0",
            1 => "EPERM",
            2 => "ENOENT",
            13 => "EACCES",
            14 => "EFAULT",
            EINVAL => "EINVAL",
            EAGAIN => "EAGAIN",
            45 => "ENOTSUP",
            ENOBUFS => "ENOBUFS",
            _ => $"errno {errno}"
        };

        [LibraryImport(LibSystem, SetLastError = true)]
        internal static partial int socket(int domain, int type, int protocol);

        [LibraryImport(LibSystem, SetLastError = true)]
        internal static partial int close(int fd);

        [LibraryImport(LibSystem, SetLastError = true)]
        internal static partial int connect(int fd, void* address, uint addressLength);

        [LibraryImport(LibSystem, SetLastError = true)]
        internal static partial int setsockopt(int fd, int level, int name, void* value, uint length);

        [LibraryImport(LibSystem, SetLastError = true)]
        internal static partial nint send(int fd, void* buffer, nuint length, int flags);

        [LibraryImport(LibSystem, SetLastError = true)]
        internal static partial nint recv(int fd, void* buffer, nuint length, int flags);

        [LibraryImport(LibSystem, SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        private static partial int sysctlbyname(string name, void* oldp, nuint* oldlenp, void* newp, nuint newlen);

        /// <summary>The plain call, correct wherever a variadic argument rides a register.</summary>
        [LibraryImport(LibSystem, EntryPoint = "ioctl", SetLastError = true)]
        private static partial int ioctl(int fd, ulong request, void* argument);

        /// <summary>The same call with six fillers, so the argument lands on the stack where an
        /// arm64 variadic caller would have put it – the first eight fill x0-x7.</summary>
        [LibraryImport(LibSystem, EntryPoint = "ioctl", SetLastError = true)]
        private static partial int ioctl_stack(int fd, ulong request, nint fill2, nint fill3, nint fill4, nint fill5, nint fill6, nint fill7, void* argument);
        #endregion
    }
}
