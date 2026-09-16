using MadWizard.Desomnia.LaunchDaemon.Native;
using System.Buffers.Binary;
using Xunit;

namespace MadWizard.Desomnia.LaunchDaemon.Tests
{
    /**
     * The two pieces of the statistics protocol that are pure reading, and the two that would fail
     * silently if they were wrong: how a datagram is divided into messages, and which descriptor
     * layout a size belongs to. Everything else in that client needs a kernel to talk to and is
     * covered by the probe under probes/TrafficProbe.MacOS instead.
     */
    public class NtStatMessagesTests
    {
        /// <summary>One message of the given type, with its length filled in the way the kernel does.</summary>
        static byte[] Message(uint type, int length, ushort flags = 0)
        {
            var message = new byte[length];

            NtStat.WriteHeader(message, context: 42, type, (ushort)length, flags);

            return message;
        }

        static byte[] Datagram(params byte[][] messages) => messages.SelectMany(message => message).ToArray();

        [Fact]
        public void AnAccumulatedDatagram_IsSplitIntoItsMessages()
        {
            // what a poll actually answers with: several flow updates packed into one packet
            var datagram = Datagram(
                Message(NtStat.MSG_SRC_UPDATE, NtStat.SRC_UPDATE_FIXED + 344),
                Message(NtStat.MSG_SRC_UPDATE, NtStat.SRC_UPDATE_FIXED + 280),
                Message(NtStat.MSG_SRC_REMOVED, 24));

            var lengths = new List<int>();
            var types = new List<uint>();

            foreach (var message in new NtStatMessages(datagram))
            {
                lengths.Add(message.Length);
                types.Add(NtStat.TypeOf(message));
            }

            Assert.Equal([NtStat.SRC_UPDATE_FIXED + 344, NtStat.SRC_UPDATE_FIXED + 280, 24], lengths);
            Assert.Equal([NtStat.MSG_SRC_UPDATE, NtStat.MSG_SRC_UPDATE, NtStat.MSG_SRC_REMOVED], types);
        }

        /**
         * The descriptor size is read as "what is left of this message", so a reader that took the
         * whole datagram for one message would compute the size of the packet — and refuse a
         * perfectly good kernel for reporting a layout nobody has heard of.
         */
        [Fact]
        public void TheDescriptorSize_IsTheMessagesOwn_NotTheDatagrams()
        {
            var datagram = Datagram(
                Message(NtStat.MSG_SRC_UPDATE, NtStat.SRC_UPDATE_FIXED + 344),
                Message(NtStat.MSG_SRC_UPDATE, NtStat.SRC_UPDATE_FIXED + 344));

            foreach (var message in new NtStatMessages(datagram))
            {
                Assert.Equal(344, message.Length - NtStat.SRC_UPDATE_FIXED);
            }
        }

        [Fact]
        public void AnUnaccumulatedMessage_IsReadWhole()
        {
            var datagram = Message(NtStat.MSG_SUCCESS, NtStat.HDR_SIZE, NtStat.FLAG_CONTINUATION);

            var messages = new List<int>();

            foreach (var message in new NtStatMessages(datagram))
            {
                messages.Add(message.Length);

                Assert.Equal(NtStat.FLAG_CONTINUATION, NtStat.FlagsOf(message));
            }

            Assert.Equal([NtStat.HDR_SIZE], messages);
        }

        [Theory]
        [InlineData(0)]         // no length at all
        [InlineData(8)]         // shorter than a header
        [InlineData(9999)]      // past the end of the datagram
        public void ALengthThatCannotBeTrue_TakesTheRest(ushort declared)
        {
            var datagram = Message(NtStat.MSG_SRC_UPDATE, 64);

            BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(12), declared);

            var messages = new List<int>();

            foreach (var message in new NtStatMessages(datagram))
            {
                messages.Add(message.Length);
            }

            Assert.Equal([64], messages); // exactly one, and no spinning
        }

        [Fact]
        public void ATrailingFragment_IsIgnored()
        {
            var datagram = Datagram(Message(NtStat.MSG_SRC_REMOVED, 24), new byte[4]);

            var messages = new List<int>();

            foreach (var message in new NtStatMessages(datagram))
            {
                messages.Add(message.Length);
            }

            Assert.Equal([24], messages);
        }

        [Fact]
        public void AnEmptyDatagram_YieldsNothing()
        {
            foreach (var message in new NtStatMessages([]))
            {
                Assert.Fail("an empty datagram has no messages");
            }
        }
    }

    /**
     * The layout table is what makes this client survivable across macOS releases: the kernel states
     * the size of its descriptors in every message, and a size with no row here is refused rather
     * than parsed at the wrong offsets.
     */
    public class NtStatLayoutTests
    {
        [Theory]
        [InlineData(NtStat.PROVIDER_TCP_KERNEL, 344)]
        [InlineData(NtStat.PROVIDER_TCP_USERLAND, 344)]
        [InlineData(NtStat.PROVIDER_QUIC_USERLAND, 344)]   // a typedef of the TCP descriptor
        [InlineData(NtStat.PROVIDER_TCP_KERNEL, 336)]
        [InlineData(NtStat.PROVIDER_UDP_KERNEL, 280)]
        [InlineData(NtStat.PROVIDER_UDP_USERLAND, 280)]
        [InlineData(NtStat.PROVIDER_UDP_KERNEL, 272)]
        public void AKnownSize_ResolvesToItsLayout(uint provider, int size)
        {
            var layout = NtStatLayout.For(provider, size);

            Assert.NotNull(layout);
            Assert.Equal(size, layout.Size);
        }

        /// <summary>macOS 15.5 answered exactly this, probe-verified — the offsets the daemon reads by.</summary>
        [Fact]
        public void TheCurrentLayout_IsWhatTheProbeMeasured()
        {
            var tcp = NtStatLayout.For(NtStat.PROVIDER_TCP_KERNEL, 344);
            var udp = NtStatLayout.For(NtStat.PROVIDER_UDP_KERNEL, 280);

            Assert.Equal((116, 120, 196), (tcp!.Pid, tcp.EffectivePid, tcp.Name));
            Assert.Equal((128, 196, 132), (udp!.Pid, udp.EffectivePid, udp.Name));
        }

        [Theory]
        [InlineData(NtStat.PROVIDER_TCP_KERNEL, 304)]   // macOS 10.15 and older: never verified, so never guessed at
        [InlineData(NtStat.PROVIDER_UDP_KERNEL, 256)]
        [InlineData(NtStat.PROVIDER_TCP_KERNEL, 352)]   // whatever comes after macOS 26
        [InlineData(NtStat.PROVIDER_TCP_KERNEL, 280)]   // a UDP size, asked of a TCP-shaped provider
        [InlineData(NtStat.PROVIDER_UDP_KERNEL, 344)]
        public void AnUnknownSize_IsRefused(uint provider, int size)
        {
            Assert.Null(NtStatLayout.For(provider, size));
        }

        [Fact]
        public void EveryFieldOfEveryLayout_FitsInsideIt()
        {
            foreach (var layout in NtStatLayout.TcpShaped.Concat(NtStatLayout.UdpShaped))
            {
                Assert.True(layout.Pid + 4 <= layout.Size, $"{layout}: pid");
                Assert.True(layout.EffectivePid + 4 <= layout.Size, $"{layout}: effective pid");
                Assert.True(layout.Name + 64 <= layout.Size, $"{layout}: name");
            }
        }

        [Fact]
        public void NoTwoLayoutsOfAShape_ShareASize()
        {
            Assert.Distinct(NtStatLayout.TcpShaped.Select(layout => layout.Size));
            Assert.Distinct(NtStatLayout.UdpShaped.Select(layout => layout.Size));
        }

        /// <summary>What the refusal tells the maintainer to add a row for.</summary>
        [Fact]
        public void TheRefusal_NamesTheSizesItWouldHaveAccepted()
        {
            var known = NtStatLayout.Describe(NtStat.PROVIDER_TCP_KERNEL);

            Assert.Contains("344", known);
            Assert.Contains("336", known);
        }
    }
}
