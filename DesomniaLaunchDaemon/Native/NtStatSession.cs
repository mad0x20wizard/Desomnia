using System.Buffers.Binary;

namespace MadWizard.Desomnia.LaunchDaemon.Native
{
    /// <summary>Handed each message of a poll, in the order the kernel produced them.</summary>
    public delegate void NtStatMessageHandler(ReadOnlySpan<byte> message);

    /// <summary>What one poll came to: how much was read, and what stopped it.</summary>
    public readonly record struct NtStatPollResult(int Messages, int Batches, int? Error, bool TimedOut)
    {
        public bool Faulted => Error is not null || TimedOut;
    }

    /**
     * The messages of one datagram.
     *
     * A poll's answer does not arrive one message to a packet: the kernel accumulates them into
     * packets of up to 4 KiB – eight or so flow updates each – and coping with that is written into
     * the interface as a requirement of a client rather than offered to it as an option. A reader
     * that mistook a datagram for a message would drop most of what it was told, and would read the
     * length of the packet where it expected the length of a descriptor.
     *
     * Whatever a message says its own length is, is where the next one starts. A length that cannot
     * be true is taken as "the rest of the datagram", which is both what a single unaccumulated
     * message looks like and the only reading that cannot spin.
     */
    public ref struct NtStatMessages
    {
        // a span cannot be captured from a primary constructor, so it is held outright
        private readonly ReadOnlySpan<byte> _datagram;

        private int _offset;

        public NtStatMessages(ReadOnlySpan<byte> datagram)
        {
            _datagram = datagram;
            _offset = 0;
        }

        public ReadOnlySpan<byte> Current { get; private set; } = default;

        public readonly NtStatMessages GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_offset + NtStat.HDR_SIZE > _datagram.Length)
                return false;

            int declared = NtStat.ReadU16(_datagram[_offset..], 12);
            int size = declared >= NtStat.HDR_SIZE && _offset + declared <= _datagram.Length ? declared : _datagram.Length - _offset;

            Current = _datagram.Slice(_offset, size);

            _offset += size;

            return true;
        }
    }

    /**
     * A connected control socket, with the two pieces of framing every caller would otherwise have
     * to know about: that a poll's answer arrives several messages to a datagram, and that it
     * arrives in batches which have to be asked for one after another.
     *
     * Owned by whoever opened it and used from one thread at a time – the meter polls from the
     * monitor's cycle, which is the only caller.
     */
    public sealed unsafe class NtStatSession : IDisposable
    {
        /// <summary>Comfortably past the largest message the protocol allows (65532).</summary>
        readonly byte[] _buffer = new byte[64 * 1024];

        /// <summary>A poll that never sees its own reply must end anyway; a stuck socket is not a hang.</summary>
        const int MaximumBatches = 512;

        int _fd = -1;
        ulong _context;

        private NtStatSession(int fd) => _fd = fd;

        public bool IsOpen => _fd >= 0;

        /**
         * Opens and connects a socket to the control.
         *
         * The receive buffer is asked to be large because everything the kernel pushes between two
         * polls waits in it – above all the final counts of flows that closed meanwhile, which are
         * the only report of the bytes they carried since the last poll. The kernel grows a control
         * socket's buffer to 256 KiB on its own; asking for more is what makes a slow monitor cycle
         * survivable, and where it is not, the kernel says so on the removal.
         */
        public static NtStatSession? Open(uint controlId, out string failure, int receiveBuffer = 1 << 20, int timeoutMilliseconds = 500)
        {
            failure = "";

            int fd = NtStat.socket(NtStat.PF_SYSTEM, NtStat.SOCK_DGRAM, NtStat.SYSPROTO_CONTROL);

            if (fd < 0)
            {
                failure = $"socket(PF_SYSTEM): {NtStat.ErrnoName(NtStat.Errno)}";

                return null;
            }

            // struct sockaddr_ctl: len, family, ss_sysaddr, sc_id, sc_unit, reserved[5]
            var address = new byte[32];

            address[0] = 32;
            address[1] = NtStat.AF_SYSTEM;

            BinaryPrimitives.WriteUInt16LittleEndian(address.AsSpan(2), NtStat.AF_SYS_CONTROL);
            BinaryPrimitives.WriteUInt32LittleEndian(address.AsSpan(4), controlId);

            fixed (byte* p = address)
            {
                if (NtStat.connect(fd, p, (uint)address.Length) != 0)
                {
                    failure = $"connect(sockaddr_ctl): {NtStat.ErrnoName(NtStat.Errno)}";

                    NtStat.close(fd);

                    return null;
                }
            }

            var session = new NtStatSession(fd);

            session.SetOption(NtStat.SO_RCVBUF, BitConverter.GetBytes(receiveBuffer));
            session.SetReceiveTimeout(timeoutMilliseconds);

            return session;
        }

        /// <summary>Subscribes to one provider; the errno it was refused with, or 0.</summary>
        public int Subscribe(uint provider, ulong filter = NtStat.FILTER_SUPPRESS_SRC_ADDED)
        {
            var context = ++_context;

            if (Send(NtStat.AddAllSources(context, provider, filter)) is int errno && errno != 0)
                return errno;

            // the reply may be behind anything the kernel was already pushing, so it is looked for
            // rather than assumed to be next
            for (var attempt = 0; attempt < 20000; attempt++)
            {
                if (Receive() is not int received)
                    return NtStat.EAGAIN;

                foreach (var message in Messages(received))
                {
                    if (NtStat.ContextOf(message) != context)
                        continue;

                    return NtStat.TypeOf(message) switch
                    {
                        NtStat.MSG_SUCCESS => 0,
                        NtStat.MSG_ERROR => NtStat.ErrorOf(message),
                        _ => NtStat.EINVAL
                    };
                }
            }

            return NtStat.EAGAIN;
        }

        /**
         * One poll of every flow, handing each message on as it is read.
         *
         * Everything the kernel pushed since the last poll is read first simply by being queued
         * first – which is how the final counts of closed flows reach the meter without anybody
         * listening in between. The continuation flag rides every request, including the first,
         * because the kernel only paces a poll for a client that asked it to.
         */
        public NtStatPollResult Poll(NtStatMessageHandler handler)
        {
            var context = ++_context;

            int messages = 0, batches = 0;

            while (batches < MaximumBatches)
            {
                if (Send(NtStat.SourceRequest(context, NtStat.MSG_GET_UPDATE, NtStat.SRC_REF_ALL, NtStat.FLAG_CONTINUATION)) is int errno && errno != 0)
                    return new NtStatPollResult(messages, batches, errno, false);

                batches++;

                bool more = false, finished = false;

                while (!finished)
                {
                    if (Receive() is not int received)
                        return new NtStatPollResult(messages, batches, null, true);

                    foreach (var message in Messages(received))
                    {
                        messages++;

                        handler(message);

                        if (NtStat.ContextOf(message) != context)
                            continue;

                        switch (NtStat.TypeOf(message))
                        {
                            case NtStat.MSG_SUCCESS:
                                more = (NtStat.FlagsOf(message) & NtStat.FLAG_CONTINUATION) != 0;
                                finished = true;
                                break;

                            case NtStat.MSG_ERROR:
                                return new NtStatPollResult(messages, batches, NtStat.ErrorOf(message), false);
                        }
                    }
                }

                if (!more)
                    break;
            }

            return new NtStatPollResult(messages, batches, null, false);
        }

        private NtStatMessages Messages(int length) => new(_buffer.AsSpan(0, length));

        /// <summary>The bytes of one datagram, or null when the receive timeout elapsed with nothing to read.</summary>
        private int? Receive()
        {
            fixed (byte* p = _buffer)
            {
                nint received = NtStat.recv(_fd, p, (nuint)_buffer.Length, 0);

                return received < NtStat.HDR_SIZE ? null : (int)received;
            }
        }

        private int Send(byte[] message)
        {
            fixed (byte* p = message)
            {
                return NtStat.send(_fd, p, (nuint)message.Length, 0) < 0 ? NtStat.Errno : 0;
            }
        }

        private void SetReceiveTimeout(int milliseconds)
        {
            // struct timeval on 64-bit macOS: time_t (8) + suseconds_t (4) + 4 padding
            var timeout = new byte[16];

            BinaryPrimitives.WriteInt64LittleEndian(timeout, milliseconds / 1000);
            BinaryPrimitives.WriteInt32LittleEndian(timeout.AsSpan(8), milliseconds % 1000 * 1000);

            SetOption(NtStat.SO_RCVTIMEO, timeout);
        }

        private void SetOption(int option, byte[] value)
        {
            fixed (byte* p = value)
            {
                NtStat.setsockopt(_fd, NtStat.SOL_SOCKET, option, p, (uint)value.Length);
            }
        }

        public void Dispose()
        {
            if (_fd >= 0)
            {
                NtStat.close(_fd);

                _fd = -1;
            }
        }
    }
}
