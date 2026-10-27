using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using MadWizard.Desomnia.Network.Capture;
using PacketDotNet;
using SharpPcap;

namespace CaptureAllocation.Benchmarks;

internal static class Program
{
    private const int BatchSize = 4096;
    private static readonly SyntheticDevice Device = new();
    private static readonly SyntheticHeader Header = new();
    private static long _sink;

    private enum Work { CaptureCopy, ParseTcp, Layers, Metadata, Payload }
    private sealed record Sample(int Round, long Packets, double Seconds, long AllocatedBytes,
        int Gen0, int Gen1, int Gen2, double GcPauseMilliseconds, long Checksum)
    {
        public double NanosecondsPerPacket => Seconds * 1e9 / Packets;
        public double BytesPerPacket => (double)AllocatedBytes / Packets;
    }
    private sealed record Result(string IP, int FrameBytes, string Work, string Variant, Sample[] Samples);

    public static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string Option(string key, string fallback)
        {
            int index = Array.IndexOf(args, key);
            return index < 0 ? fallback : args[index + 1];
        }
        int repeats = int.Parse(Option("--repeats", "9"));
        double seconds = double.Parse(Option("--seconds", "0.4"));
        int[] sizes = Option("--sizes", "128,1514,9014").Split(',').Select(int.Parse).ToArray();
        Work[] workloads = Option("--work", string.Join(',', Enum.GetNames<Work>())).Split(',')
            .Select(name => Enum.Parse<Work>(name, ignoreCase: true)).ToArray();
        bool[] versions = Option("--ip", "4,6").Split(',').Select(v => v switch
        {
            "4" => false, "6" => true, _ => throw new ArgumentException("IP must be 4 or 6")
        }).ToArray();
        if (repeats < 1 || seconds <= 0 || sizes.Any(s => s < 100 || s > 9014))
            throw new ArgumentException("Use positive samples/duration and frame sizes from 100 to 9014.");

        var sourcePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../modules/NetworkMonitor/Monitor/Capture/NetworkCapture.cs"));
        string sourceHash = File.Exists(sourcePath)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath))) : "source unavailable";
        var environment = new
        {
            DateUtc = DateTime.UtcNow, Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
            LogicalProcessors = Environment.ProcessorCount, ServerGC = GCSettings.IsServerGC,
            Latency = GCSettings.LatencyMode.ToString(),
            TieredCompilationOverride = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            TieredCompilationProjectDefault = false,
            SharpPcap = typeof(PacketCapture).Assembly.GetName().Version?.ToString(),
            PacketDotNet = typeof(Packet).Assembly.GetName().Version?.ToString(),
            StructSize = Unsafe.SizeOf<NetworkCapture>(), SourceSha256 = sourceHash,
            Repeats = repeats, MinimumSampleSeconds = seconds, BatchSize,
            CorpusFrames = 256
        };
        Console.WriteLine(JsonSerializer.Serialize(environment, new JsonSerializerOptions { WriteIndented = true }));

        if (args.Contains("--allocation-costs"))
        {
            PrintAllocationCosts();
            return 0;
        }

        Verify();
        Console.WriteLine("Verification passed: parsed layers, payload, class/struct equivalence, shared caches, allocation delta.");
        if (args.Contains("--verify-only")) return 0;

        List<Result> results = [];
        foreach (bool ipv6 in versions)
        foreach (int size in sizes)
        {
            var corpus = CreateCorpus(size, ipv6);
            foreach (Work work in workloads)
            {
                bool baseline = work is Work.CaptureCopy or Work.ParseTcp;
                var structSamples = new List<Sample>();
                var classSamples = new List<Sample>();
                // Warm both bodies. Tiered compilation is off by default, so no delayed promotion.
                Measure(corpus, work, false, -1, Math.Max(0.15, seconds));
                if (!baseline) Measure(corpus, work, true, -1, Math.Max(0.15, seconds));
                for (int round = 0; round < repeats; round++)
                {
                    // AB / BA order limits systematic temperature/time/order bias.
                    if (!baseline && round % 2 != 0)
                        classSamples.Add(Measure(corpus, work, true, round, seconds));
                    structSamples.Add(Measure(corpus, work, false, round, seconds));
                    if (!baseline && round % 2 == 0)
                        classSamples.Add(Measure(corpus, work, true, round, seconds));
                }
                string ip = ipv6 ? "IPv6" : "IPv4";
                results.Add(new(ip, size, work.ToString(), baseline ? "Baseline" : "Struct", structSamples.ToArray()));
                if (!baseline)
                    results.Add(new(ip, size, work.ToString(), "Class", classSamples.OrderBy(s => s.Round).ToArray()));
                Print(ip, size, work, structSamples, baseline ? null : classSamples);
            }
        }
        string output = Path.GetFullPath(Option("--output", "results.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { Environment = environment, Results = results },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Saved {output}; checksum sink={Volatile.Read(ref _sink)}");
        return 0;
    }

    private static double Median(IEnumerable<double> source)
    {
        var values = source.Order().ToArray();
        return (values[(values.Length - 1) / 2] + values[values.Length / 2]) / 2;
    }

    private static object? _allocationSink;

    private static void PrintAllocationCosts()
    {
        static void MeasureGetter(string name, Func<object> getter)
        {
            const int count = 100_000;
            for (int i = 0; i < 10_000; i++) Volatile.Write(ref _allocationSink, getter());
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < count; i++) Volatile.Write(ref _allocationSink, getter());
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine($"{name}: {(double)bytes / count:F0} B/access");
        }
        foreach (bool ipv6 in new[] { false, true })
        {
            var ethernet = (EthernetPacket)Packet.ParsePacket(LinkLayers.Ethernet, CreateCorpus(1514, ipv6)[0]);
            var ip = ethernet.Extract<IPPacket>();
            var address = ip.SourceAddress;
            _ = ethernet.Extract<TcpPacket>();
            string version = ipv6 ? "IPv6" : "IPv4";
            MeasureGetter($"{version} source IPAddress", () => ip.SourceAddress);
            MeasureGetter($"{version} source PhysicalAddress including backing array", () => ethernet.SourceHardwareAddress);
            MeasureGetter($"{version} IPServiceEndPoint using existing address", () => new IPServiceEndPoint(address, IPProtocol.TCP, 445));
            MeasureGetter($"{version} uncached SourceEndPoint including fresh address", () => ip.SourceEndPoint);
        }
    }

    private static void Print(string ip, int size, Work work, List<Sample> value, List<Sample>? reference)
    {
        double ns = Median(value.Select(s => s.NanosecondsPerPacket));
        string first = $"{ip} {size,4} {work,-11}: {ns,7:F1} ns, {Median(value.Select(s => s.BytesPerPacket)),7:F0} B/pkt, {size / ns:F2} GB/s";
        if (reference is null) { Console.WriteLine(first); return; }
        double classNs = Median(reference.Select(s => s.NanosecondsPerPacket));
        Console.WriteLine(first + $" | class {classNs:F1} ns, {Median(reference.Select(s => s.BytesPerPacket)):F0} B/pkt; class time delta {(classNs / ns - 1) * 100:+0.0;-0.0;0.0}%");
    }

    private static Sample Measure(byte[][] corpus, Work work, bool useClass, int round, double seconds)
    {
        // Outside the timed interval; sample includes subsequent naturally occurring collections.
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        TimeSpan pause = GC.GetTotalPauseDuration();
        long start = Stopwatch.GetTimestamp(), now = start, packets = 0, checksum = 0;
        do
        {
            checksum += RunBatch(corpus, work, useClass);
            packets += BatchSize;
            now = Stopwatch.GetTimestamp();
        } while ((now - start) / (double)Stopwatch.Frequency < seconds);
        double elapsed = (now - start) / (double)Stopwatch.Frequency;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var result = new Sample(round, packets, elapsed, bytes, GC.CollectionCount(0) - gen0,
            GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2,
            (GC.GetTotalPauseDuration() - pause).TotalMilliseconds, checksum);
        Volatile.Write(ref _sink, checksum);
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long RunBatch(byte[][] corpus, Work work, bool useClass)
    {
        long sum = 0;
        for (int i = 0; i < BatchSize; i++)
        {
            var capture = new PacketCapture(Device, Header, corpus[i & (corpus.Length - 1)]);
            RawCapture raw = capture.GetPacket(); // Real SharpPcap: timestamp, RawCapture, span.ToArray().
            if (work == Work.CaptureCopy)
            {
                sum += ConsumeRaw(raw);
                continue;
            }
            var ethernet = (EthernetPacket)Packet.ParsePacket(raw.LinkLayerType, raw.Data);
            if (work == Work.ParseTcp)
                sum += ConsumePacket(ethernet);
            else if (useClass)
                sum += ConsumeClass(new NetworkCaptureClass(ethernet), work);
            else
            {
                var value = new NetworkCapture(ethernet);
                sum += ConsumeStruct(ref value, work);
            }
        }
        return sum;
    }

    // Matching non-inlined consumer boundaries keep the class allocation observable to the JIT.
    // This is NOT a call-dispatch benchmark: both paths perform identical work once per packet.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long ConsumeRaw(RawCapture raw) => raw.Data.Length + raw.Data[0];

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long ConsumePacket(EthernetPacket packet) => packet.Extract<TcpPacket>().PayloadLength;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long ConsumeStruct(ref NetworkCapture capture, Work work)
    {
        long value = capture.PayloadLength + capture.FindPacket<TcpPacket>()!.DestinationPort;
        if (work >= Work.Metadata)
            value += capture.SourcePhysicalAddress.GetHashCode() + (long)capture.TargetPhysicalAddress.GetHashCode()
                + capture.SourceEndPoint!.Port + capture.TargetEndPoint!.Port + (capture.IsIPUnicast ? 1 : 0);
        if (work == Work.Payload)
        {
            var payload = capture.PayloadData;
            value += payload.Length + payload[0] + payload[^1];
        }
        return value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long ConsumeClass(NetworkCaptureClass capture, Work work)
    {
        long value = capture.PayloadLength + capture.FindPacket<TcpPacket>()!.DestinationPort;
        if (work >= Work.Metadata)
            value += capture.SourcePhysicalAddress.GetHashCode() + (long)capture.TargetPhysicalAddress.GetHashCode()
                + capture.SourceEndPoint!.Port + capture.TargetEndPoint!.Port + (capture.IsIPUnicast ? 1 : 0);
        if (work == Work.Payload)
        {
            var payload = capture.PayloadData;
            value += payload.Length + payload[0] + payload[^1];
        }
        return value;
    }

    private static byte[][] CreateCorpus(int size, bool ipv6)
    {
        var sourceMac = PhysicalAddress.Parse("001122334455");
        var targetMac = PhysicalAddress.Parse("AABBCCDDEEFF");
        var sourceIP = IPAddress.Parse(ipv6 ? "2001:db8::10" : "192.0.2.10");
        var targetIP = IPAddress.Parse(ipv6 ? "2001:db8::20" : "192.0.2.20");
        var random = new Random(42);
        var corpus = new byte[256][];
        for (int i = 0; i < corpus.Length; i++)
        {
            byte[] payload = new byte[size - 14 - (ipv6 ? 40 : 20) - 20];
            random.NextBytes(payload);
            var tcp = new TcpPacket((ushort)(50000 + i), 445)
            {
                Acknowledgment = true, SequenceNumber = (uint)(i * payload.Length), PayloadData = payload
            };
            IPPacket ip = ipv6 ? new IPv6Packet(sourceIP, targetIP) : new IPv4Packet(sourceIP, targetIP);
            ip.PayloadPacket = tcp;
            var ethernet = new EthernetPacket(sourceMac, targetMac, ipv6 ? EthernetType.IPv6 : EthernetType.IPv4)
            {
                PayloadPacket = ip
            };
            ethernet.UpdateCalculatedValues();
            tcp.UpdateTcpChecksum();
            if (ip is IPv4Packet ip4) ip4.UpdateIPChecksum();
            corpus[i] = ethernet.Bytes;
            if (corpus[i].Length != size) throw new InvalidOperationException("Incorrect generated frame length");
        }
        return corpus;
    }

    private static void Verify()
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        var structFields = typeof(NetworkCapture).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(f => (f.Name, Type: f.FieldType.Name)).OrderBy(f => f.Name).ToArray();
        var classFields = typeof(NetworkCaptureClass).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(f => (f.Name, Type: f.FieldType.Name)).OrderBy(f => f.Name).ToArray();
        Require(structFields.SequenceEqual(classFields), "Class control fields differ from production struct");
        foreach (bool ipv6 in new[] { false, true })
        foreach (int size in new[] { 100, 128, 1514, 9014 })
        {
            byte[][] corpus = CreateCorpus(size, ipv6);
            var raw = new PacketCapture(Device, Header, corpus[0]).GetPacket();
            Require(!ReferenceEquals(raw.Data, corpus[0]) && raw.Data.AsSpan().SequenceEqual(corpus[0]), "Capture must own a copied buffer");
            var packet = (EthernetPacket)raw.GetPacket();
            Require(packet.Extract<TcpPacket>().PayloadLength == size - 14 - (ipv6 ? 40 : 20) - 20, "Wrong TCP payload length");
            foreach (Work work in new[] { Work.Layers, Work.Metadata, Work.Payload })
            {
                var value = new NetworkCapture(packet);
                var reference = new NetworkCaptureClass(packet);
                Require(ConsumeStruct(ref value, work) == ConsumeClass(reference, work), "Different consumers");
                Require(ReferenceEquals(value.SourceEndPoint, value.SourceEndPoint), "Struct cache lost");
                Require(ReferenceEquals(reference.SourceEndPoint, reference.SourceEndPoint), "Class cache lost");
                Require(value.PayloadData.AsSpan().SequenceEqual(reference.PayloadData), "Different payload");
                Require(RunBatch(corpus, work, false) == RunBatch(corpus, work, true), "Different replay checksums");
            }
        }
        var allocationCorpus = CreateCorpus(1514, false);
        RunBatch(allocationCorpus, Work.Metadata, false);
        RunBatch(allocationCorpus, Work.Metadata, true);
        long before = GC.GetAllocatedBytesForCurrentThread();
        RunBatch(allocationCorpus, Work.Metadata, false);
        long structBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        RunBatch(allocationCorpus, Work.Metadata, true);
        long classBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        long delta = (classBytes - structBytes) / BatchSize;
        Require(delta >= Unsafe.SizeOf<NetworkCapture>(), "Class allocation was eliminated or measurement is invalid");
        Console.WriteLine($"Wrapper allocation delta: {delta} bytes/packet; struct storage: {Unsafe.SizeOf<NetworkCapture>()} bytes");
    }
}
