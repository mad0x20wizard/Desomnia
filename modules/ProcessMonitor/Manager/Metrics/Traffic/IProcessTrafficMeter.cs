namespace MadWizard.Desomnia.Processes.Manager.Metrics
{
    /**
     * What a platform must bring to measure per-process traffic: somewhere for an account to
     * subscribe, and somewhere to unsubscribe when the process it belongs to is gone.
     *
     * The seam exists because the two platforms that can measure this at all measure it in
     * opposite ways. Windows is told: a kernel trace session pushes an event per transfer, and the
     * meter's whole job is to book what arrives. macOS is asked: the kernel keeps running totals
     * per flow and hands them over on request, so its meter polls and books the differences. What
     * they have in common is exactly this interface and the account behind it – which is why
     * <see cref="ProcessTrafficAccount"/> lives here rather than once per platform, subtle parts
     * and all.
     *
     * Registering an implementation is what turns the metric on for a platform; a platform with no
     * way to measure registers none, and <see cref="IProcessMetricSupport"/> is what tells the
     * configuration so.
     */
    public interface IProcessTrafficMeter
    {
        /**
         * Takes the account on and reports whether the meter is actually running – the account may
         * only answer for a process once this returned true, because a number nobody maintains
         * reads as a process that stopped transferring.
         *
         * Idempotent: an account whose meter died re-subscribes on its next sample, which is also
         * where a torn-down meter gets its restart.
         */
        bool StartReading(ProcessTrafficAccount account);

        /// <summary>Drops the account – only its own registration, a successor under a reused pid stays.</summary>
        void StopReading(ProcessTrafficAccount account);

        /**
         * Brings the accounts up to date, for a platform whose counters are asked for rather than
         * pushed. Called by every account as it is read, so a meter that polls should collapse the
         * asks of one monitor cycle into a single one.
         *
         * Nothing by default: where the kernel pushes, the accounts are already current, and the
         * call is the price of not having two kinds of account.
         */
        void Refresh() { }
    }
}
