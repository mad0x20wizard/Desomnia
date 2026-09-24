using MadWizard.Desomnia.Network.Naming;
using MadWizard.Desomnia.Network.Naming.Messages;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.SleepProxy.Registration;
using Makaretu.Dns;
using Microsoft.Extensions.Logging;

using static MadWizard.Desomnia.Network.Naming.Messages.DNSMessage;

namespace MadWizard.Desomnia.Network.SleepProxy
{
    /// <summary>
    /// Answers DNS-SD browse requests the way an Apple Bonjour Sleep Proxy (BSP) would: it advertises
    /// the proxy service itself (<c>_sleep-proxy._udp.local</c>) and the services that watched hosts
    /// have asked us to advertise on their behalf while they are asleep / unreachable.
    /// </summary>
    internal class SleepProxyResolver(NetworkHost proxy, SleepProxyService service) : SleepProxyRegistrationBuffer(service.Port), IMulticastDNSResolver
    {
        public required ILogger<SleepProxyResolver> Logger { private get; init; }

        public required SleepProxyRegistrar Registrar { private get; init; }

        /// <summary>This proxy's DNS-SD instance name, e.g. "10-10-10-10 desktop._sleep-proxy._udp.local".</summary>
        private DomainName Instance => new([$"{service.Metrics} {proxy.Name}", .. service.LocalDomainName.Labels]);

        void IMulticastDNSResolver.Announce(DNSMessage announcement)
        {
            Logger.LogInformation("Advertising as Sleep Proxy via {Port} with metrics: '{Metrics}'", service.Port, service.Metrics);

            announcement.AnswerWith(proxy, service, Instance);
        }

        void IMulticastDNSResolver.Resolve(DNSQuery query)
        {
            foreach (var question in query.Questions)
            {
                // Service-type enumeration (RFC 6763 §9): make the sleep-proxy type discoverable to
                // generic DNS-SD browsers, just as a real Bonjour Sleep Proxy advertises itself.
                if (question.Name == MakaretuDnsExt.ServiceEnumeration && question.Type is DnsType.PTR or DnsType.ANY)
                {
                    query.AnswerWith(MakaretuDnsExt.ServiceEnumeration, service.LocalDomainName);

                    continue;
                }

                // A browse (PTR) for the sleep-proxy type, or a targeted SRV/TXT for our instance.
                query.AnswerWith(question, proxy, service, Instance);
            }
        }

        void IMulticastDNSResolver.Goodbye(DNSMessage goodbye)
        {
            goodbye.AnswerWith(proxy, service, Instance, AnswerOptions.Goodbye);

            Logger.LogInformation("Stopped advertising as Sleep Proxy");
        }

        /// <summary>
        /// Handles a Sleep Proxy registration: a DNS UPDATE whose OPT record carries an EDNS0 Owner
        /// option. The records to defend are in the UPDATE (authority) section; the wake info is in
        /// the Owner option. The <see cref="SleepProxyRegistrationBuffer"/> base has already collected and
        /// merged the (possibly multi-message) registration at this point.
        /// </summary>
        protected override void ProcessRegistration(DNSUpdate update, SleepProxyRegistration reg)
        {
            try
            {
                if (Registrar.Register(reg, out var lease))
                {
                    Logger.LogDebug("Registration of '{Name}' successful; granting lease: {Duration}", reg.Name, lease.Duration);

                    update.AnswerWithLease(lease.Duration);

                    lease.Ended += (sender, args) =>
                    {
                        if (args.HasFailed)
                            Logger.LogDebug("Handoff from '{Name}' has failed", reg.Name);
                        else
                            Logger.LogDebug("Lease for '{Name}' has {Verb}", reg.Name,
                                args.HasExpired ? "expired" : "ended");
                    };
                }
                else // no new registration was created, we merely confirm the existing one
                {
                    update.AnswerWithLease(lease.GrantedUntil - DateTime.Now);
                }
            }
            catch (FormatException ex)
            {
                Logger.LogDebug(ex, "Registration for '{Name}' was invalid.", reg.Name);

                update.AnswerWithError(MessageStatus.FormatError);
            }
            catch (SystemException ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                Logger.LogDebug(ex, "Registration for '{Name}' was refused.", reg.Name);

                update.AnswerWithError(MessageStatus.Refused);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Registration of '{Name}' failed.", reg.Name);

                update.AnswerWithError(MessageStatus.ServerFailure);
            }

            RespondTo(update);
        }
    }
}
