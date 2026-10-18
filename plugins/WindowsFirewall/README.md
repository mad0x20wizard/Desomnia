# Windows Firewall prototype

This plugin supplies the Windows Filtering Platform (WFP) backend for the platform-neutral [FirewallMonitor module](../../modules/FirewallMonitor/FirewallMonitor.csproj). The core module projects the physical local host's configured static service filters into firewall rules. It is a first implementation for testing source authorization and per-service default blocking. The interfaces remain provisional; the broader design is in [the living specification](../../docs/FIREWALL_MONITOR_SPECIFICATION.md).

## Startup and shutdown

The application hosts register the FirewallMonitor core module after platform plugins. When an IFirewallManager backend is registered, the module creates one FirewallMonitor per network; otherwise it stays inactive. The Windows plugin registers only its backend. Its INetworkService.AfterStartup callback reads the discovered local addresses and static ServiceFilterWatch instances, compiles their filters, and installs the snapshot. Installation deliberately happens after network startup discovery, without changing discovery order.

IFirewallManager.Install takes a NetworkHost and a collection of FirewallRule records, and returns a disposable policy lease. Shutdown or scope disposal releases that lease. The monitor, compiler, FirewallRule model and IFirewallManager contract live in modules/FirewallMonitor, targeting net10.0. That project depends on NetworkMonitor and contains no WFP code. The Windows plugin references FirewallMonitor, accepts only LocalHost, and has no dependency on DesomniaService.

Each lease uses a dynamic WFP session and its own sublayer. Filters are committed in one transaction; failed installation is rolled back. Closing the session removes its filters and sublayer. Dynamic sessions also provide native cleanup when their owning process terminates. [WFP object management](https://learn.microsoft.com/en-us/windows/win32/fwp/object-management)

## Rule semantics

Each native rule selects a local destination address, TCP or UDP, a destination port, and an inclusive remote source address range. IPv4 and IPv6 use their respective ALE_AUTH_RECV_ACCEPT layers. There is no host-wide fallback rule.

| Rule | Weight | Action |
| --- | --- | --- |
| Effective exclusion or matching top-level MustNot | 300 | Block |
| Combined positive source ranges of applicable Must rules | 200 | Permit |
| Any Must in the service's effective rule list | 100 | Block all sources for that service |

All three categories share the policy's sublayer, whose weight is 0x8000. Higher filter weights win within that sublayer. Permits use the default soft-permit behavior: another firewall provider may still block the connection. An existing Windows Firewall allowance for the test application or service is therefore needed for remote testing; this plugin does not create that allowance or override administrative restrictions. [WFP filter arbitration](https://learn.microsoft.com/en-us/windows/win32/fwp/filter-arbitration)

Nested source Must rules are alternatives; nested MustNot rules exclude sources from their enclosing transport predicate. Those nested exclusions are not independent global blocks. The compiler combines all applicable Must alternatives before emitting exclusions as direct blocks above broad source permits. For SSH from everyone except 192.168.128.52, it emits one permit for all IPv4 sources and one block for that exact address, plus the service fallback. It does not create permits for the two inverse ranges. If another Must authorizes part of an excluded range, only the remaining exclusion is blocked. Independent top-level MustNot predicates retain their veto, including over independent host permits. Adjacent equivalent intervals are merged.

## Supported configuration

The prototype accepts transport service rules, with literal StaticHostFilterRule and StaticHostRangeFilterRule source restrictions. Literal host and range rules at network or host scope also participate in each watched service's effective policy. TCP-derived payload rules contribute their transport and source restrictions only. The service filter's rule list includes inherited network and host transport rules. RouterFilter contributes no rules.

Unsupported top-level rules (including EveryHost, ForeignHost and ping rules) and named dynamic source dependencies cause a visible startup error before installing this network's snapshot. This deliberately bounds the first implementation; it is not complete coverage of the specification. Custom composite filters with independent rule-group semantics are not supported.

Virtual hosts, gateways, host-wide FilterOptions defaults, dynamic services, resource subscriptions, and later address changes are deferred. Restart or reload the network context to refresh this snapshot. Existing connections and UDP flow reauthorization need separate validation; the tests open fresh flows. The backend does not yet constrain rules by interface identity, so identical local addresses on different interfaces are not distinguished.

## Build and load

Build from the repository root:

~~~powershell
dotnet build plugins/WindowsFirewall/WindowsFirewall.csproj -c Release
~~~

Use a host built from the same source revision, because the host must include the new DesomniaFirewallMonitor assembly. Copy the plugin output from plugins/WindowsFirewall/bin/Release/net10.0-windows into a WindowsFirewall subdirectory of the host's plugin search directory, alongside the existing plugin folders. The loader expects WindowsFirewall/WindowsFirewall.dll. Installing the plugin activates the prototype on all monitored networks; no extra XML module element is required. The host process needs permission to manage WFP, normally through an elevated account or its service identity.

[example.xml](example.xml) configures watched TCP 8443 and UDP 8444 services. Replace Ethernet and the documentation-only source addresses with the actual interface and test-client addresses. The example does not start a server or enable automatic sleep.

For TCP 8443, sources in 192.0.2.0/24 are permitted except 192.0.2.13, which has an explicit MustNot block. Other sources are blocked by the service fallback. UDP 8444 permits only 192.0.2.10. Unwatched ports retain their existing system policy.

## Validation

Projection and lifecycle tests:

~~~powershell
dotnet test tests/FirewallMonitor.Tests/FirewallMonitor.Tests.csproj
~~~

Run the native tests from an elevated Windows shell:

~~~powershell
dotnet test tests/WindowsFirewall.Tests/WindowsFirewall.Tests.csproj
~~~

The native tests use temporary listeners on ephemeral loopback ports. They check fallback blocking, source matching, permit priority, explicit deny priority, an unaffected control port, IPv6, UDP, and access restoration after session disposal. Without elevation they are explicitly skipped, not counted as passing native validation. They do not change Windows Firewall profiles or persistent administrative rules.

For the complete remote path, start test listeners on the example's ports and confirm access before loading the plugin. Then test an authorized source, an unauthorized source, and the explicitly denied source. Also verify an unrelated port, and repeat after stopping Desomnia to confirm its restrictions disappear. Use new connections for each check. The automatic loopback tests complement this remote test; they do not establish interaction with every system firewall policy.

The plugin and all three application hosts build. The core tests cover direct exclusions, overlapping alternatives, independent host permits and vetoes, IPv4/IPv6 boundaries, and comparison with PacketRuleFilter over generated TCP/UDP policies. The user has manually verified IPv4 SSH exclusion and that Desomnia permits do not bypass Windows Firewall default or explicit blocks. Automated native tests still require an elevated test process. The full NetworkMonitor suite has five failures also reproduced against the committed baseline: DisposalPreventsNewEntries, DisposedRangesAndContextsAreNoLongerSwept, and three NetworkInterfaceMonitor selector tests. They are unrelated to this prototype.
