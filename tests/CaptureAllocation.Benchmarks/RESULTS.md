# NetworkCapture struct versus class results

Measured on 22 October 2026, Windows x64, AMD Ryzen 9 7900 (12 cores, 24 logical processors), .NET 10.0.12, Release JIT. SharpPcap 6.3.1 and PacketDotNet 1.4.8 match NetworkMonitor. The production capture source SHA256 is recorded in each raw result. Only one thread performs the timed copying/parsing.

**Recommendation: use a class at the application/plugin boundaries unless saving a few percent of this managed parsing workload is an explicit performance requirement.** The struct produces a real, measurable reduction in allocation. It does not make this an allocation-free capture path, and it does not establish that `ref` must propagate throughout the program. Keeping the struct within a bounded synchronous section is also reasonable if that section can maintain one canonical mutable cache without complicating other APIs.

## Allocation result

The actual production struct occupies **128 bytes** on this x64 runtime. The mechanically equivalent class allocates **144 bytes** per packet. This delta was the same for every frame size, IP version, and access pattern. The class allocation was not optimized away.

For ordinary 1514-byte Ethernet/IPv4/TCP frames:

| Cumulative managed work | Struct or baseline bytes/packet | Class bytes/packet |
| --- | ---: | ---: |
| SharpPcap raw capture, timestamp, owned frame copy | 1616 | same baseline |
| Above plus Ethernet/IP/TCP parsing | 2552 | same baseline |
| Above plus wrapper and lazy layer/length reads | 2552 | 2696 |
| Above plus both MAC addresses and both endpoints | 2824 | 2968 |
| Above plus a materialized transport payload array | 4312 | 4456 |

The capture copy dominates the bytes allocated for normal and jumbo frames. Decoding the packet tree adds 936 bytes/packet here; MAC/IP/endpoint metadata adds another 272; materializing the 1460-byte TCP payload adds 1488. The struct saves 144. These are measured increments in this workload, not universal object-size estimates for all protocols.

IPv6 metadata costs 2952 bytes with the struct and 3096 with the class; IPv6 payload materialization costs 4416 versus 4560. MAC/IP/endpoint caching and avoiding payload copies remain useful for **both** wrapper representations.

## What this means at the requested rate

These projections treat GB as decimal and count captured Ethernet bytes, excluding FCS, preamble, and inter-frame gap. The wrapper is constructed once per captured packet.

| Captured frame bytes | Packets/s at 1 GB/s | Packets/s at 2.5 GB/s | Allocation saved at 1 GB/s | Allocation saved at 2.5 GB/s |
| --- | ---: | ---: | ---: | ---: |
| 128, small-packet control | 7.81 million | 19.53 million | 1125 MB/s | 2812.5 MB/s |
| 1514, MTU 1500 | 660,502 | 1,651,255 | 95.1 MB/s | 237.8 MB/s |
| 9014, MTU 9000 | 110,939 | 277,346 | 16.0 MB/s | 39.9 MB/s |

For 1514-byte IPv4 metadata packets at 2.5 GB/s, the complete measured allocation rate projects to **4.66 GB/s with the struct versus 4.90 GB/s with the class**. The wrapper therefore accounts for about 4.9% of class-path allocated bytes. With jumbo frames, it is about 1.4%; with payload materialization on ordinary frames, about 3.2%.

If 1–2.5 GB/s means **file payload** rather than captured frames, divide by TCP payload size instead: for the IPv4 frames used here, the wrapper saving becomes approximately 98.6–246.6 MB/s before ACKs or other traffic. ACKs and smaller packets increase captures per useful byte. Packet rate, not byte rate alone, determines wrapper allocation pressure. If only a fraction of captured packets receives a wrapper, multiply the savings by that fraction.

## Timing result

The first complete workstation-GC run used nine alternating-order rounds of at least 400 ms per sample. It copied/parsed 888,598,528 measured packets, totaling **1.548 TB** of replayed frame bytes across all workloads and variants (excluding warmup). Values below are medians, not best-case samples.

| Frame bytes | IP | Work | Struct ns/packet | Class ns/packet | Extra class time |
| --- | --- | --- | ---: | ---: | ---: |
| 128 | IPv4 | Metadata | 251.5 | 266.5 | 6.0% |
| 1514 | IPv4 | Layers | 207.3 | 212.8 | 2.7% |
| 1514 | IPv4 | Metadata | 293.9 | 306.0 | 4.1% |
| 1514 | IPv6 | Metadata | 299.5 | 313.7 | 4.7% |
| 1514 | IPv4 | Payload | 353.0 | 362.1 | 2.6% |
| 9014 | IPv4 | Metadata | 567.9 | 597.2 | 5.2% |
| 9014 | IPv6 | Metadata | 564.5 | 594.7 | 5.3% |
| 9014 | IPv4 | Payload | 824.0 | 842.3 | 2.2% |

For 1514-byte IPv4 metadata, this corresponds to **5.15 GB/s versus 4.95 GB/s** of single-thread managed replay. At 2.5 GB/s, the measured difference projects to roughly **0.020 core-seconds per second**, or two percentage points of one core. This projection excludes everything outside the benchmark. It is not a guarantee that the whole application can sustain 2.5 GB/s. For 128-byte metadata packets, neither representation reached even 1 GB/s in this single-thread replay.

Paired samples fluctuate: for the first run's 1514-byte IPv4 metadata case, individual class-time deltas ranged from -1.8% to +10.2%. Allocation deltas are stable; exact timing percentages are hardware, GC, JIT, and workload dependent. The raw JSON and `Summarize.ps1` preserve and expose that spread.

A second full run matched the daemon's workstation, **non-concurrent GC** configuration. It replayed another 915,034,112 measured packets / 1.610 TB. Metadata results were:

| Frame bytes | IP | Struct ns/packet | Class ns/packet | Extra class time |
| --- | --- | ---: | ---: | ---: |
| 1514 | IPv4 | 289.9 | 296.2 | 2.2% |
| 1514 | IPv6 | 287.7 | 301.2 | 4.7% |
| 9014 | IPv4 | 541.5 | 549.4 | 1.4% |
| 9014 | IPv6 | 544.0 | 563.9 | 3.7% |

The allocation counts were identical to the first run. Changing this GC setting did not turn the wrapper choice into a large throughput difference. Both are Windows JIT measurements; neither is a measurement of a Linux NativeAOT deployment.

A fresh-process confirmation used eleven 750 ms samples per variant for the metadata workload, with background workstation GC restored:

| Frame bytes | IP | Struct ns/packet | Class ns/packet | Extra class time |
| --- | --- | ---: | ---: | ---: |
| 1514 | IPv4 | 303.5 | 313.9 | 3.4% |
| 1514 | IPv6 | 312.0 | 325.2 | 4.2% |
| 9014 | IPv4 | 573.5 | 570.0 | -0.6% |
| 9014 | IPv6 | 616.7 | 624.4 | 1.3% |

This supports a modest, repeatable benefit for ordinary full-size frames on this machine, not a guaranteed benefit of the same size for jumbo frames. The slightly faster class result in one jumbo case is another reason not to overinterpret a single few-percent measurement.

```powershell
$env:DOTNET_gcServer = '0'
$env:DOTNET_gcConcurrent = '1'
dotnet run --project tests/CaptureAllocation.Benchmarks -c Release --no-build -- --work Metadata --sizes 1514,9014 --repeats 11 --seconds 0.75 --output tests/CaptureAllocation.Benchmarks/results/confirmation-local.json
Remove-Item Env:DOTNET_gcServer, Env:DOTNET_gcConcurrent
```

## Garbage collection

For 1514-byte metadata packets in the first run, Gen-0 collections per million packets decreased from about 177 to 169 for IPv4 and 185 to 176 for IPv6 when using the struct. There were no Gen-2 collections in these samples. Total GC pause time did **not** consistently decrease: IPv4 measured approximately 2.77 ms/million packets for the struct versus 2.26 for the class. Do not turn the allocation reduction into a claim of guaranteed lower pause latency.

Short-lived allocations are relatively cheap in this workload. Survivorship and queue backlog can change GC behavior substantially. The benchmark collects before each sample and immediately discards processed captures, so it cannot predict long-lived daemon heap behavior, worst-case pauses, or dropped packets.

## Implications for the current design

`NetworkCapture` is a normal mutable struct, not a `ref struct`, and not an unmanaged value that can be used with `stackalloc`. A local passed by `ref` avoids its own object allocation. Its fifteen managed reference fields still point to heap objects. An array, queue, boxed interface value, or containing class can put the struct itself on the heap too.

Passing a class normally copies one object reference. Passing this struct by `ref` passes a managed reference to its storage. Neither copies all 128 bytes at every call. The benchmark uses one non-inlined consumer boundary for each representation; it measures construction, parsing, metadata access, and allocation effects rather than a synthetic method-call loop.

The reason to require `ref` here is also semantic: getters write caches. An accidental value copy, readonly receiver, or `in` parameter can initialize an independent copy and cause repeated allocations later. The class naturally shares one cache across ordinary references. Neither representation makes concurrent cache mutation safe.

The current code has several relevant boundaries:

- `PacketCaptureQueue.EnqueueCapture` calls `PacketCapture.GetPacket()` before queuing an owned `RawCapture`; `NetworkDevice.ProcessQueuedPackets` parses on one consumer thread. Making the later wrapper a struct cannot remove the earlier buffer copy, raw capture, or parsed packet objects. Queue synchronization, retention, and cross-thread access are absent from this experiment.
- `NetworkMonitor.HandlePacket` processes services while holding `Network.Mutex`. Wrapper representation does not remove that serialization. Multiple fast parse loops alone cannot establish throughput for the complete monitor.
- `DemandService` still creates `CaptureSummary`; other services still consume `EthernetPacket`. `ReachabilityService`, for example, reads `ip.SourceAddress` more than once. Sharing one lazy cache across those consumers may avoid redundant metadata materialization regardless of whether it is a struct or class.
- `DefaultFilterContextParser.Parse` returns newly allocated context snapshots. The context types retain packet/address references rather than a borrowed capture. Those allocations remain with either design and make the wrapper a smaller fraction of total work. Its history overload and `TcpPayloadReassembly` consume `Span<NetworkCapture>`; a span need not refer to stack storage.
- `DemandRequestBuffer` currently retains `EthernetPacket` in a channel and queue across async work. If histories start retaining captures, ownership and copy/cache semantics must be explicit. Dense arrays store a 128-byte struct inline on the heap; an equivalent class array uses an 8-byte reference plus a 144-byte object per entry, roughly 24 extra bytes/entry, not a 144-byte difference in retained history size.
- `HTTPFilterContextParser` materializes payload and creates protocol parsing state/strings. It is a different, more expensive workload than counting bytes during an established file transfer. Avoid applying application-level payload parsing to every data segment unless required.

An `IFilterContextParser` whose method takes `ref NetworkCapture` does not itself box the capture: the interface receiver is the parser. Converting the capture itself to an ordinary capture interface would generally introduce boxing. The current benchmark compares concrete struct-by-ref with concrete class-by-value and does not claim to measure every possible interface dispatch or JIT devirtualization choice.

The bounded synchronous struct design is defensible if its measured savings matter to the budget. For interfaces spanning providers, plugins, retained histories, and asynchronous work, a shared class is easier to use correctly, and this experiment supports accepting its modest cost. Reducing duplicate metadata construction, avoiding unnecessary payload copies, and profiling queue/locking costs should precede a broad ref-based API change justified solely by throughput.

## Reproduction and sources

### Estimated savings from sharing NetworkCapture across consumers

A separate `--allocation-costs` probe on the same runtime measured these uncached costs:

| Operation | IPv4 | IPv6 |
| --- | ---: | ---: |
| Read a PacketDotNet IP address | 40 B | 80 B |
| Read a PacketDotNet MAC address, including backing array | 64 B | 64 B |
| Construct TCPEndPoint with an existing address | 32 B | 32 B |
| Read the uncached SourceEndPoint extension, including fresh address | 72 B | 112 B |

The estimates below assume one shared canonical capture across all consumers, an established TCP packet whose source is known to ReachabilityService, and both endpoints and both MACs materialized. Wrapper allocation is excluded from both sides. They count actual executed accesses, not the number of configured rules that may be skipped.

CaptureSummary already allocates two IP addresses and two MAC addresses: 208 B for IPv4 or 288 B for IPv6. On the known-source path, ReachabilityService additionally reads the source IP twice and source MAC once: 144 B or 224 B. Sharing the capture eliminates those duplicates, but the new endpoint-based API adds two 32-byte endpoint objects. Thus the base net saving is **80 B/packet IPv4 or 160 B/packet IPv6**, not the full cost of every metadata object.

| Executed work in the old path | Estimated net saving IPv4 | Estimated net saving IPv6 |
| --- | ---: | ---: |
| CaptureSummary plus known-source ReachabilityService | 80 B/packet | 160 B/packet |
| Above plus one successful PacketExt.IsIPUnicast call | 240 B/packet | 480 B/packet |
| Above plus two more IP address reads, e.g. host rule and router peer lookup | 320 B/packet | 640 B/packet |

The old successful IsIPUnicast reads the destination three times and source once; the capture reuses its cached addresses. Each additional executed host/address lookup adds another 40 B IPv4 or 80 B IPv6 to potential savings. If the target MAC is never needed, lazy capture access can save another 64 B relative to eager CaptureSummary.

For 1514-byte frames at 1–2.5 GB/s captured traffic, the illustrated IPv4 cases save approximately **53–211 MB/s at 1 GB/s** or **132–528 MB/s at 2.5 GB/s**. IPv6 cases save **106–423 MB/s** or **264–1057 MB/s**, respectively. These are scenarios, not an observed average of the user's traffic/configuration.

Light paths with an unknown source, no repeated filtering, or early rejection may save much less; adding endpoints where none were previously needed can offset or exceed the removed allocations. New filter-context objects from the separate filter-engine rewrite are not included. Additional payload-copy avoidance, trace behavior, and ARP/NDP/knock processing are also excluded from these established-TCP estimates. The parsed packet tree and owned frame allocation remain. Subtract 144 B/packet if comparing the old struct summary against a new heap wrapper; leave it out when evaluating cache reuse independently of wrapper representation.

See [README.md](README.md) for exact workload definitions, limitations, verification, and commands. Raw samples: [workstation.json](results/workstation.json), [daemon-gc.json](results/daemon-gc.json), and [confirmation.json](results/confirmation.json).

The synthetic capture uses the installed implementations corresponding to [SharpPcap PacketCapture.GetPacket](https://github.com/dotpcap/sharppcap/blob/bfedf297e7410ffcf44e05d14f7fbef304f20895/SharpPcap/PacketCapture.cs) and [RawCapture's span-copy constructor](https://github.com/dotpcap/sharppcap/blob/bfedf297e7410ffcf44e05d14f7fbef304f20895/SharpPcap/RawCapture.cs). No production files were changed by this experiment.

## Reassessment for 100 byte captures

Here 100 bytes means the actual average captured Ethernet frame length, not 100 bytes of application payload or a snaplen truncation of larger frames. At the same captured-byte rates, 100-byte frames imply **10 million packets/s at 1 GB/s** and **25 million packets/s at 2.5 GB/s**. That is 15.14 times the packet rate of 1514-byte frames. Wire overhead is excluded, consistent with the preceding calculations.

The migration estimates per packet are unchanged because the address/endpoint objects do not become smaller with a shorter TCP payload:

| Old-path scenario, with wrapper representation excluded | IPv4 saved per packet | Saved at 1 GB/s | Saved at 2.5 GB/s | IPv6 saved per packet | Saved at 1 GB/s | Saved at 2.5 GB/s |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Summary plus known-source reachability | 80 B | 0.8 GB/s | 2.0 GB/s | 160 B | 1.6 GB/s | 4.0 GB/s |
| Above plus one successful unicast check | 240 B | 2.4 GB/s | 6.0 GB/s | 480 B | 4.8 GB/s | 12.0 GB/s |
| Above plus two additional address reads | 320 B | 3.2 GB/s | 8.0 GB/s | 640 B | 6.4 GB/s | 16.0 GB/s |

These are projected reductions in allocated heap bytes at the target input rate, not a claim that one processing thread can reach that rate. They retain the earlier known-source/filter-access assumptions and already subtract the two new endpoint objects. New filter contexts and other allocations outside metadata reuse are excluded.

The separate struct-versus-class choice saves another **144 B/packet**, corresponding to **1.44 GB/s** or **3.60 GB/s** of allocations at those packet rates. For a light IPv4 scenario saving only 80 B through reuse, changing the old struct summary to a class while adopting the new cache would add a net 64 B/packet. Shared caching and wrapper representation should therefore remain separate accounting decisions.

The fresh 100-byte replay measured the following allocation costs, including one-time metadata materialization but excluding any duplicate old-consumer accesses:

| Cumulative managed workload | IPv4 struct/baseline | IPv4 class | IPv6 struct/baseline | IPv6 class |
| --- | ---: | ---: | ---: | ---: |
| Capture copy and raw capture | 200 B | same | 200 B | same |
| Parsed Ethernet/IP/TCP and layer-cache accesses | 1136 B | 1280 B | 1184 B | 1328 B |
| Above plus both MACs and both endpoints | 1408 B | 1552 B | 1536 B | 1680 B |

At 2.5 GB/s of 100-byte frames, IPv4 metadata replay therefore projects to **35.2 GB/s allocated with the struct** versus **38.8 GB/s with the class**, even after shared caching. IPv6 projects to **38.4 versus 42.0 GB/s**. The class wrapper represents about 9.3% (IPv4) or 8.6% (IPv6) of those allocations. The existing packet object model, per-packet work, and allocation rate are major architectural considerations at 10–25 million packets/s; cache reuse alone does not establish line-rate capacity.

The workstation-GC run measured these metadata timings (nine paired rounds, at least 400 ms each):

| IP | Struct ns/packet | Class ns/packet | Extra class time | Struct throughput | Class throughput |
| --- | ---: | ---: | ---: | ---: | ---: |
| IPv4 | 341.6 | 360.5 | 5.5% | 2.93 million packets/s | 2.77 million packets/s |
| IPv6 | 349.1 | 364.1 | 4.3% | 2.86 million packets/s | 2.75 million packets/s |

This is approximately 0.28–0.29 GB/s for struct metadata processing on one thread, before production queue/locking/filter work. The input target of 1–2.5 GB/s was not achieved in this replay. Projection to that target describes the allocation burden if the required packet throughput were achieved; it does not assert linear scaling with more threads. Timing reflects this run's machine conditions, so cross-run comparisons with earlier 128-byte results should not be attributed to frame length alone. Raw samples: [100-byte-workstation.json](results/100-byte-workstation.json).

A second metadata-only run used the daemon's non-concurrent workstation GC (nine paired rounds of at least 500 ms). IPv4 measured **252.7 ns struct / 261.4 ns class**, and IPv6 **251.1 / 261.4 ns**, approximately **4 million packets/s or 0.40 GB/s** with the struct. Allocated bytes were identical. The class added 3.4% and 4.1% time respectively. This run also fell below the target; the absolute timing difference between runs should not be assigned solely to GC without further controlled repetition. Raw samples: [100-byte-daemon-gc.json](results/100-byte-daemon-gc.json).

If 100 bytes instead means retaining only the first 100 bytes of a full-sized packet, the packet rate is still determined by the original traffic's frame-size distribution. For an original 1514-byte frame, the metadata/wrapper savings per second remain at the earlier 1514-byte estimates; only capture-buffer copying/storage shrinks. This benchmark builds complete, valid 100-byte packets and does not test truncated-frame parser behavior.

```powershell
dotnet run --project tests/CaptureAllocation.Benchmarks -c Release --no-restore -- --sizes 100 --repeats 9 --seconds 0.4 --output tests/CaptureAllocation.Benchmarks/results/100-byte-local.json
```
