using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Handoff.Registration;
using MadWizard.Desomnia.Network.Naming.Options;
using MadWizard.Desomnia.Network.Neighborhood;
using Makaretu.Dns;
using NetTools;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.SleepProxy.Registration
{
    /// <summary>
    /// A parsed Sleep Proxy registration (a DNS UPDATE plus its EDNS0 Owner option): the records a sleeping
    /// host asked us to keep alive on its behalf, and how to wake it again.
    /// </summary>
    public class SleepProxyRegistration : HandoffRegistration
    {
        public byte             Version         { get; init; } = 0;
        /// <summary>The Owner option's sequence number (the host increments it on each registration).</summary>
        public byte             Sequence        { get; init; } = 0;

        public PhysicalAddress? VirtualAddress  { get; init; }

        /// <summary>Optional lease duration, from EDS0 Lease option </summary>
        public TimeSpan         RequestedLease  { get; init; }

        // extracted from records:

        public string           Name            { get; init; }
        public string           Hostname        { get; init; }

        private SleepProxyRegistration(NetworkHost host) : base(host.PhysicalAddress ?? throw new NotSupportedException($"Host {host.Name} has not MAC address configured."))
        {
            Name = host.Name;
            Hostname = host.HostName;

            if (host is VirtualNetworkHost virtualHost)
            {
                VirtualAddress = virtualHost.PhysicalHost.PhysicalAddress;
            }
        }

        public SleepProxyRegistration(NetworkHost host, HandoffOptions options, byte sequence) : this(host)
        {
            foreach (var ip in host.SelectIPAddressesBy(options))
            {
                IPAddresses[ip] = host[ip];
            }

            RequestedLease = options.Duration;
            Password = options.Password;

            Sequence = sequence;
        }

        public SleepProxyRegistration(string name, string hostname, EdnsOwnerOption owner, EdnsLeaseOption lease) : base(owner.PrimaryMac)
        {
            Name = name;
            Hostname = hostname;

            Version = owner.Version;
            Sequence = owner.Sequence;

            VirtualAddress = owner.WakeupMac;
            Password = owner.Password;

            RequestedLease = lease.Duration;
        }

        internal IEnumerable<EdnsOption> Options
        {
            get
            {
                yield return new EdnsOwnerOption
                {
                    Version = Version,
                    Sequence = Sequence,
                    PrimaryMac = PhysicalAddress,
                    WakeupMac = VirtualAddress,
                    Password = Password,
                };

                yield return new EdnsLeaseOption { Duration = RequestedLease };

                foreach (var service in Services)
                {
                    // The friendly service name travels privately between Desomnia peers, not as a TXT
                    // attribute a third-party proxy would re-advertise on the link.
                    yield return new EdnsServiceInfoOption
                    {
                        ServiceDomainName = service.Service.LocalDomainName,
                        Name = service.Name,
                    };

                    var option = new EdnsServiceFilterOption { ServiceDomainName = service.Service.LocalDomainName };

                    foreach (var host in service.HostFilterRule)
                    {
                        if (host.IsDynamic)
                            option.Filters.Add(new DynamicHostFilterEntry   { Type = host.Type, Name = host.Name! });
                        else foreach(var ip in host.IPAddresses)
                            option.Filters.Add(new StaticHostFilterEntry    { Type = host.Type, Address = ip });
                    }

                    foreach (var range in service.HostRangeFilterRule)
                    {
                        if (range is LocalRangeFilterRuleInfo)
                            option.Filters.Add(new LocalRangeFilterEntry    { Type = range.Type });
                        else if (range.AddressRange is IPAddressRange addressRange)
                            option.Filters.Add(new StaticRangeFilterEntry   { Type = range.Type, Range = addressRange });
                    }

                    if (option.Filters.Count > 0)
                        yield return option;
                }
            }
        }

        /// <summary>
        /// Whether <paramref name="other"/> describes substantially the same registration as this one: the same host
        /// identity (MAC, wake target, names) defending the same addresses and services. The transient negotiation
        /// fields (message <see cref="Id"/>, <see cref="Sequence"/>, requested lease, password) are intentionally ignored.
        /// </summary>
        internal bool Matches(SleepProxyRegistration other)
        {
            return Equals(Name, other.Name)
                && Equals(Hostname, other.Hostname)
                && Equals(PhysicalAddress, other.PhysicalAddress)
                && Equals(VirtualAddress, other.VirtualAddress)
                && SamePassword(Password, other.Password)
                && IPAddresses.Keys.ToHashSet().SetEquals(other.IPAddresses.Keys)
                && ServiceSignatures().SetEquals(other.ServiceSignatures());
        }

        // The password is a fresh byte[] on every parse, so it must be compared by content, not reference.
        private static bool SamePassword(byte[]? a, byte[]? b) => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);

        private HashSet<string> ServiceSignatures() => [.. Services.Select(s => $"{s.InstanceName}|{s.Service.LocalDomainName}|{s.Port}")];

        public static explicit operator SleepProxyRegistration(Message message) => SleepProxyRegistrationFormat.ParseUpdateMessage(message);
        public static explicit operator Message(SleepProxyRegistration reg)     => SleepProxyRegistrationFormat.BuildUpdateMessage(reg);
    }
}
