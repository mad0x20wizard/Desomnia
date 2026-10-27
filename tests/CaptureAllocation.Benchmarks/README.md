# NetworkCapture allocation experiment

This standalone .NET 10 console project compares the current production `NetworkCapture` struct, passed by `ref`, with an otherwise equivalent sealed class. It measures capture copying and Ethernet/IP/TCP parsing, rather than repeatedly passing an already constructed wrapper between methods. It does not modify or depend on building the in-progress application refactor.

See [RESULTS.md](RESULTS.md) for the measured results and design recommendation. Raw measurements are in [results](results).

## Run

From the repository root, using the .NET 10 SDK and PowerShell:

```powershell
dotnet run --project tests/CaptureAllocation.Benchmarks -c Release -p:NuGetAudit=false -- --verify-only
dotnet run --project tests/CaptureAllocation.Benchmarks -c Release --no-restore -- --output tests/CaptureAllocation.Benchmarks/results/local.json
```

To measure individual uncached address/endpoint allocations without a timed replay:

```powershell
dotnet run --project tests/CaptureAllocation.Benchmarks -c Release --no-build -- --allocation-costs
```

This warms each getter, measures 100,000 accesses, and stores each result in an escaping reference. Packet construction and delegate creation are outside the allocation measurement. It supports the migration estimates in `RESULTS.md`; those estimates are not measurements of a completed application migration.

The default full matrix takes roughly three minutes. For a short run:

```powershell
dotnet run --project tests/CaptureAllocation.Benchmarks -c Release --no-build -- --ip 4 --sizes 1514 --repeats 5 --seconds 0.2 --output tests/CaptureAllocation.Benchmarks/results/quick.json
```

Use a fresh process with these environment variables to match `DesomniaDaemon.csproj`'s workstation, non-concurrent GC. Restore any prior values afterwards if they were already set:

```powershell
$env:DOTNET_gcServer = '0'
$env:DOTNET_gcConcurrent = '0'
dotnet run --project tests/CaptureAllocation.Benchmarks -c Release --no-build -- --output tests/CaptureAllocation.Benchmarks/results/daemon-local.json
Remove-Item Env:DOTNET_gcServer, Env:DOTNET_gcConcurrent
```

Summarize an existing run, including paired timing ranges and GC pause fractions:

```powershell
& tests/CaptureAllocation.Benchmarks/Summarize.ps1 tests/CaptureAllocation.Benchmarks/results/local.json | Format-Table
```

`--ip` accepts `4`, `6`, or `4,6`. `--sizes` accepts comma-separated captured frame lengths from 100 to 9014. `--work` selects comma-separated workload names from the table below. `--seconds` is the minimum duration of each sample, and `--repeats` controls samples per case. Run benchmarks sequentially without a debugger or competing CPU-heavy work. Tiered compilation is disabled in the project to use optimized JIT code throughout; set `DOTNET_TieredCompilation=1` to investigate production tiering/PGO separately. NativeAOT needs a separate published benchmark.

## What is measured

Each packet follows this path:

```text
prebuilt Ethernet frame in a reusable 256-frame corpus
    -> real SharpPcap.PacketCapture over ReadOnlySpan<byte>
    -> real PacketCapture.GetPacket(): RawCapture + timestamp + copied byte[]
    -> real PacketDotNet.Packet.ParsePacket
    -> force lazy TCP decoding
    -> production struct OR generated class
    -> one non-inlined consumer, with identical accesses
```

SharpPcap 6.3.1 and PacketDotNet 1.4.8 are the same versions resolved by NetworkMonitor. Only device metadata and the capture header are synthetic. No NIC or Npcap driver is needed. `SyntheticHeader.Timeval` allocates a timestamp with a fixed time; native header construction and marshalling are excluded. The actual SharpPcap `GetPacket()` implementation copies the span into an owned array.

Production source files are linked into the project. At build time, `Generate-Class.ps1` produces the class control inside `obj`, changing only the type name/declaration, struct constructor initializer, and struct-only `readonly` accessor modifier. It records the source SHA256 and fails if the expected source shape changes. Both variants have identical fields and caching logic. Non-inlined consumers prevent the JIT from eliminating the class allocation across the call boundary, as could happen in an unrealistically trivial local-only example.

| Workload | Work per captured packet |
| --- | --- |
| CaptureCopy | SharpPcap buffer copy and raw capture only; allocation baseline |
| ParseTcp | Above plus Ethernet/IP/TCP decoding; allocation baseline |
| Layers | Above plus wrapper, lazy layer lookup, payload length, destination port |
| Metadata | Layers plus both MAC addresses, both endpoints, unicast classification |
| Payload | Metadata plus materializing the transport payload array |

Every case uses fresh raw captures, byte arrays, decoded packet trees, and wrappers. Parsed packets and initialized caches are never reused across timed packets. The corpus includes varying ports, sequence numbers, and payload bytes. Frames use TCP without options, VLAN tags, fragmentation, or IPv6 extension headers. 1514 bytes means Ethernet plus a 1500-byte IP packet; 9014 means a 9000-byte IP packet. 128 bytes is a small-packet control, not minimum Ethernet size.

The verification mode checks that SharpPcap actually copies the frame, TCP payload lengths match, class/struct fields and consumers agree, repeated cache reads retain identity, replay checksums match, and the class allocation survives JIT optimization. These checks run before every measurement run too.

## Measurement method

Each case warms both paths. Nine measured rounds alternate struct/class and class/struct order. Samples run for at least 400 ms in batches of 4096 packets, with one clock check per batch. The returned checksum is consumed. All allocations in the measured path occur on the measurement thread and are counted with `GC.GetAllocatedBytesForCurrentThread()`.

Full collections run **before**, outside, each timed sample. Naturally occurring collections and their pauses during the sample remain included. The JSON preserves packet counts, duration, allocated bytes, generation collection counts, aggregate GC pause duration, and checksum for every round. The summary reports medians and paired ranges; these are descriptive observations, not a claim of universal statistical significance. GC pause fraction is total process GC pause time divided by timed wall time, not a per-packet latency percentile. Packet and byte totals represent actual repeated copying/parsing, not an allocation-rate estimate alone.

Throughput uses decimal GB/s of captured frame bytes: `frame bytes / ns per packet`. The input is replayed at maximum speed rather than throttled to a target rate. Target-rate allocation projections use `target bytes/s / frame bytes * allocated bytes/packet`. GB/s is bytes, not Gbit/s; 1–2.5 GB/s corresponds to 8–20 Gbit/s before additional wire overhead.

## Scope

The comparison isolates the wrapper's incremental cost within real managed capture/parsing work. It excludes driver/kernel capture, BPF, native callbacks/header creation, the producer/consumer queue, cross-thread buffer transfer, monitor locking, host lookup, filter context creation, application protocol interpretation, logging, async retention, and reassembly. Payload materialization copies bytes but does not scan/interpret the complete file contents.

The reusable source corpus is cache-friendly, and captured graphs are discarded immediately. A production queue backlog or long-lived request can change cache behavior, object survival, promotion, and pause times. Explicit pre-sample collections also do not reproduce a long-running daemon's heap history. Absolute GB/s values are managed replay capacity, not a guarantee of lossless NIC throughput. Allocation deltas are much more portable than timing differences. Measure the complete deployed pipeline separately before committing to a line-rate target.
