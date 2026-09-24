using MadWizard.Desomnia.Network.Naming;
using MadWizard.Desomnia.Network.Naming.Options;
using Makaretu.Dns;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.SleepProxy.Registration
{
    /// <summary>
    /// A <see cref="DNSService"/> that collects the DNS UPDATE messages of a Sleep Proxy
    /// registration burst (same message id and owner MAC) into a
    /// <see cref="SleepProxyRegistrationMessageBurst"/>, and hands the merged result to
    /// <see cref="ProcessRegistration"/> -- a registration split over multiple messages is thus
    /// processed (and answered) as ONE. Bursts from Desomnia clients carry an
    /// <see cref="EdnsPagingOption"/> on every message and complete the moment all pages arrived
    /// (duplicate deliveries of a page -- an update raced to several proxy addresses, or observed
    /// by both the capture and the socket inlet -- are recognized and ignored); bursts from Apple
    /// clients are bounded by a collection window, closed early when a message no longer looks
    /// "full" (a client only splits when it runs out of space, so every message of a burst except
    /// the last is nearly full). A burst still missing announced pages when its window ends is
    /// logged and dropped here -- half a registration never reaches the handling downstream.
    /// </summary>
    internal abstract class SleepProxyRegistrationBuffer(ushort port, string? realm = null) : DNSService(port, realm)
    {
        /// <summary>
        /// Fixed window from the burst's FIRST message: generous against the microseconds a
        /// back-to-back burst spans on a LAN, yet safely under Apple's 1 s retransmission timer.
        /// </summary>
        private static readonly TimeSpan CollectionWindow = TimeSpan.FromMilliseconds(500);

        /// <summary>How long late copies of an already processed burst are still recognized (and ignored).</summary>
        private static readonly TimeSpan CompletionWindow = TimeSpan.FromSeconds(5);

        /// <summary>Messages smaller than this are considered the (possibly only) tail of a burst.</summary>
        private const int ContinuationSizeThreshold = 1200;

        private const int MaxPendingBursts = 8;

        private readonly Dictionary<(ushort Id, PhysicalAddress Owner), Pending> _pending = [];

        private readonly Dictionary<(ushort Id, PhysicalAddress Owner), DateTime> _completed = [];

        protected sealed override void ProcessUpdate(DNSUpdate update)
        {
            WireLogger.LogTrace("Received a dynamic DNS update from {Endpoint}", update.SourceEndpoint);

            // Without an Owner option this can't be a Sleep Proxy registration -- refuse right away.
            if (update.Request.Options.OfType<EdnsOwnerOption>().FirstOrDefault() is not EdnsOwnerOption owner)
            {
                WireLogger.LogDebug("DNS UPDATE without an EDNS0 Owner option");

                update.AnswerWithError(MessageStatus.FormatError);

                RespondTo(update);

                return;
            }

            if (Collect(update, owner, out var pending))
            {
                Process(pending);
            }
        }

        /// <summary>A complete registration, merged from its burst; answer via <paramref name="update"/>.</summary>
        protected abstract void ProcessRegistration(DNSUpdate update, SleepProxyRegistration registration);

        /// <summary>
        /// Files <paramref name="update"/> under its burst. Returns <c>true</c> when the burst is
        /// ready to be processed right away; otherwise the message is parked until either the rest
        /// of the burst arrives or the collection window ends.
        /// </summary>
        private bool Collect(DNSUpdate update, EdnsOwnerOption owner, [NotNullWhen(true)] out Pending? pending)
        {
            byte? pages = update.Request.Options.OfType<EdnsPagingOption>().FirstOrDefault()?.Count;

            bool tail = pages is null && update.MessageLength < ContinuationSizeThreshold;

            lock (_pending)
            {
                var key = (update.Request.Id, owner.PrimaryMac);

                // Late copies of a burst we just processed (raced addresses, double inlet delivery,
                // retransmissions) must not start a new collection.
                if (_completed.TryGetValue(key, out var when) && DateTime.Now - when < CompletionWindow)
                {
                    pending = null;
                    return false;
                }

                if (_pending.TryGetValue(key, out var collecting))
                {
                    if (collecting.Burst.Sequence == owner.Sequence)
                    {
                        collecting.Burst.Add(update.Request); // ignores pages already held

                        if (collecting.Burst.IsSatisfied || tail)
                        {
                            _pending.Remove(key);
                            collecting.CancelExpiry();

                            Complete(key);

                            pending = collecting;
                            return true;
                        }

                        pending = null;
                        return false;
                    }

                    // A different sequence under the same key means a new registration attempt has
                    // begun; flush what we hold and start over.
                    _pending.Remove(key);
                    Flush(collecting);
                }

                pending = new Pending(new SleepProxyRegistrationMessageBurst(update.Request), update);

                // A lone small message carrying address records is a complete single-message
                // registration (a reordered burst tail would carry no addresses).
                if (pending.Burst.IsSatisfied || (tail && update.Request.AuthorityRecords.OfType<AddressRecord>().Any()))
                {
                    Complete(key);

                    return true;
                }

                if (_pending.Count >= MaxPendingBursts) // bounded: flush the oldest, ready or not
                {
                    var oldest = _pending.OrderBy(entry => entry.Value.Began).First();

                    _pending.Remove(oldest.Key);
                    Flush(oldest.Value);
                }

                _pending[key] = pending;

                _ = ExpireLater(key, pending);

                pending = null;
                return false;
            }
        }

        /// <summary>Remembers a processed burst for <see cref="CompletionWindow"/>, so its late copies are ignored.</summary>
        private void Complete((ushort Id, PhysicalAddress Owner) key)
        {
            var now = DateTime.Now;

            foreach (var expired in _completed.Where(entry => now - entry.Value >= CompletionWindow).ToList())
                _completed.Remove(expired.Key);

            _completed[key] = now;
        }

        private async Task ExpireLater((ushort Id, PhysicalAddress Owner) key, Pending pending)
        {
            try
            {
                await Task.Delay(CollectionWindow, pending.Expiry.Token);
            }
            catch (OperationCanceledException)
            {
                return; // the burst completed on its own
            }

            lock (_pending)
            {
                if (!_pending.Remove(key))
                    return; // completed (or flushed) concurrently
            }

            await ProcessDetached(pending);
        }

        private void Flush(Pending pending)
        {
            pending.CancelExpiry();

            _ = ProcessDetached(pending);
        }

        /// <summary>Processing off the packet loop: the network mutex must be acquired first.</summary>
        private async Task ProcessDetached(Pending pending)
        {
            using (await Network.Mutex.LockAsync())
            {
                Process(pending);
            }
        }

        private void Process(Pending pending)
        {
            var update = pending.ResponseTarget;
            var burst = pending.Burst;

            // A burst still missing announced messages is dropped -- nothing downstream could make
            // sense of half a registration; the client will notice through its response timeout.
            if (burst.IsIncomplete)
            {
                WireLogger.LogWarning("Dropping an incomplete registration burst from {Endpoint}: received {Count} of {Expected} message(s).",
                    update.SourceEndpoint, burst.Count, burst.ExpectedCount);

                return;
            }

            SleepProxyRegistration registration;

            try
            {
                registration = (SleepProxyRegistration)burst.Merge();
            }
            catch (FormatException ex)
            {
                WireLogger.LogDebug(ex, "Received a malformed registration from {Endpoint}.", update.SourceEndpoint);

                update.AnswerWithError(MessageStatus.FormatError);

                RespondTo(update);

                return;
            }
            catch (Exception ex)
            {
                WireLogger.LogError(ex, "Could not parse registration from {Endpoint}.", update.SourceEndpoint);

                update.AnswerWithError(MessageStatus.ServerFailure);

                RespondTo(update);

                return;
            }

            ProcessRegistration(update, registration);
        }

        /// <summary>A burst still being collected, together with the update the single response goes to.</summary>
        private sealed class Pending(SleepProxyRegistrationMessageBurst burst, DNSUpdate response)
        {
            internal DateTime Began { get; } = DateTime.Now;

            internal CancellationTokenSource Expiry { get; } = new();

            public SleepProxyRegistrationMessageBurst Burst => burst;

            /// <summary>The update the single response for the whole burst is sent to.</summary>
            public DNSUpdate ResponseTarget => response;

            internal void CancelExpiry() => Expiry.Cancel();
        }
    }
}
