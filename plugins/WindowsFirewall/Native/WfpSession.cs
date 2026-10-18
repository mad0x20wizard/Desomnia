using MadWizard.Desomnia.Firewall.Manager;
using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.WindowsFilteringPlatform;

namespace MadWizard.Desomnia.Firewall.WindowsFirewall.Native
{
    /// <summary>
    /// One dynamic session owns a network's startup policy and its private sublayer.
    /// Closing it (including process termination) removes permits and fallback blocks.
    /// </summary>
    internal sealed class WfpSession : SafeHandleZeroOrMinusOneIsInvalid
    {
        private WfpSession(HANDLE engine) : base(ownsHandle: true) => SetHandle(engine.Value);

        protected override bool ReleaseHandle() => PInvoke.FwpmEngineClose0((HANDLE)handle) == 0;

        internal static unsafe WfpSession Install(string name, IReadOnlyCollection<FirewallRule> rules)
        {
            foreach (var rule in rules)
            {
                if (rule.Service.Protocol is not (IPProtocol.TCP or IPProtocol.UDP)
                    || rule.LocalAddress.AddressFamily != rule.Source.Begin.AddressFamily
                    || rule.LocalAddress.AddressFamily != rule.Source.End.AddressFamily)
                    throw new ArgumentException("A firewall rule must select TCP or UDP and one IP address family.", nameof(rules));
            }

            string label = $"Desomnia: {name}";
            fixed (char* displayName = label)
            {
                FWPM_SESSION0 session = new()
                {
                    displayData = new() { name = new PWSTR(displayName) },
                    flags = PInvoke.FWPM_SESSION_FLAG_DYNAMIC
                };
                HANDLE engine;
                Check(PInvoke.FwpmEngineOpen0(default, 10 /* RPC_C_AUTHN_WINNT */, null, &session, &engine), "open session");
                var owner = new WfpSession(engine);
                try
                {
                    Check(PInvoke.FwpmTransactionBegin0(owner, 0), "begin transaction");
                    try
                    {
                        FWPM_SUBLAYER0 sublayer = new()
                        {
                            subLayerKey = Guid.NewGuid(),
                            displayData = new() { name = new PWSTR(displayName) },
                            weight = 0x8000
                        };
                        Check(PInvoke.FwpmSubLayerAdd0(owner, in sublayer, default), "add sublayer");

                        foreach (var rule in rules)
                            owner.AddRule(sublayer.subLayerKey, rule);

                        Check(PInvoke.FwpmTransactionCommit0(owner), "commit transaction");
                    }
                    catch
                    {
                        _ = PInvoke.FwpmTransactionAbort0(owner);
                        throw;
                    }
                    return owner;
                }
                catch
                {
                    owner.Dispose();
                    throw;
                }
            }
        }

        private unsafe void AddRule(Guid sublayer, FirewallRule rule)
        {
            bool ipv4 = rule.LocalAddress.AddressFamily == AddressFamily.InterNetwork;
            FWP_BYTE_ARRAY16 local6 = default, first6 = default, last6 = default;
            FWP_RANGE0 range = new()
            {
                valueLow = AddressValue(rule.Source.Begin, &first6),
                valueHigh = AddressValue(rule.Source.End, &last6)
            };
            var local = AddressValue(rule.LocalAddress, &local6);

            FWPM_FILTER_CONDITION0* conditions = stackalloc FWPM_FILTER_CONDITION0[4];
            conditions[0] = new()
            {
                fieldKey = PInvoke.FWPM_CONDITION_IP_LOCAL_ADDRESS,
                matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
                conditionValue = new() { type = local.type }
            };
            if (ipv4)
                conditions[0].conditionValue.Anonymous.uint32 = local.Anonymous.uint32;
            else
                conditions[0].conditionValue.Anonymous.byteArray16 = &local6;

            conditions[1] = new()
            {
                fieldKey = PInvoke.FWPM_CONDITION_IP_PROTOCOL,
                matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
                conditionValue = new() { type = FWP_DATA_TYPE.FWP_UINT8 }
            };
            conditions[1].conditionValue.Anonymous.uint8 = rule.Service.Protocol == IPProtocol.TCP ? (byte)6 : (byte)17;
            conditions[2] = new()
            {
                fieldKey = PInvoke.FWPM_CONDITION_IP_LOCAL_PORT,
                matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
                conditionValue = new() { type = FWP_DATA_TYPE.FWP_UINT16 }
            };
            conditions[2].conditionValue.Anonymous.uint16 = rule.Service.Port;
            conditions[3] = new()
            {
                fieldKey = PInvoke.FWPM_CONDITION_IP_REMOTE_ADDRESS,
                matchType = FWP_MATCH_TYPE.FWP_MATCH_RANGE,
                conditionValue = new() { type = FWP_DATA_TYPE.FWP_RANGE_TYPE }
            };
            conditions[3].conditionValue.Anonymous.rangeValue = &range;

            ulong weight = (ulong)rule.Priority;
            string label = $"Desomnia: {rule.Action} {rule.LocalAddress}:{rule.Service} from {rule.Source} ({rule.Priority})";
            fixed (char* displayName = label)
            {
                FWPM_FILTER0 filter = new()
                {
                    filterKey = Guid.NewGuid(),
                    displayData = new() { name = new PWSTR(displayName) },
                    layerKey = ipv4 ? PInvoke.FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V4 : PInvoke.FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V6,
                    subLayerKey = sublayer,
                    weight = new() { type = FWP_DATA_TYPE.FWP_UINT64 },
                    numFilterConditions = 4,
                    filterCondition = conditions,
                    // Soft permits: other system firewall providers may still block.
                    action = new() { type = rule.Action == FirewallAction.Permit
                        ? FWP_ACTION_TYPE.FWP_ACTION_PERMIT : FWP_ACTION_TYPE.FWP_ACTION_BLOCK }
                };
                filter.weight.Anonymous.uint64 = &weight;
                Check(PInvoke.FwpmFilterAdd0(this, in filter, default, null), $"add {label}");
            }
        }

        private static unsafe FWP_VALUE0 AddressValue(IPAddress address, FWP_BYTE_ARRAY16* ipv6)
        {
            FWP_VALUE0 value = default;
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                value.type = FWP_DATA_TYPE.FWP_UINT32;
                value.Anonymous.uint32 = BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());
            }
            else
            {
                value.type = FWP_DATA_TYPE.FWP_BYTE_ARRAY16_TYPE;
                address.GetAddressBytes().CopyTo(new Span<byte>(ipv6, 16));
                value.Anonymous.byteArray16 = ipv6;
            }
            return value;
        }

        private static void Check(uint error, string operation)
        {
            if (error != 0)
                throw new Win32Exception(unchecked((int)error), $"WFP could not {operation} (0x{error:X8}).");
        }
    }
}
