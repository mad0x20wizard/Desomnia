namespace MadWizard.Desomnia.Processes.Manager
{
    /**
     * Which of the counters the platform underneath actually keeps.
     *
     * Every platform answers this for itself, in its own project, because it is the only place that
     * knows: whether the display kernel accounts graphics time per process, whether the kernel keeps
     * a byte ledger, whether anything meters a process' traffic at all. The module measuring them
     * holds none of that knowledge and does not degrade around it – a configuration that asks for a
     * counter the machine does not keep is refused where it is read, not quietly ignored for the
     * lifetime of the service.
     *
     * A platform that gains a counter widens <see cref="SupportedMetrics"/> and nothing else changes.
     */
    public interface IProcessMetricSupport
    {
        /// <summary>The counters this platform's processes can be asked for.</summary>
        ProcessMetric SupportedMetrics { get; }
    }

    /// <summary>The per-process counters a threshold can be measured against, one per min attribute.</summary>
    [Flags]
    public enum ProcessMetric
    {
        None        = 0,

        /// <summary>minCPU – <see cref="IProcess.ProcessorTime"/>.</summary>
        Processor   = 1 << 0,
        /// <summary>minGPU – <see cref="IProcess.GraphicsProcessorTime"/>.</summary>
        Graphics    = 1 << 1,
        /// <summary>minIO – <see cref="IProcess.StorageData"/>.</summary>
        Storage     = 1 << 2,
        /// <summary>minTraffic – <see cref="IProcess.NetworkData"/>.</summary>
        Traffic     = 1 << 3,
    }

    /**
     * What a process answers for when no platform took over: <see cref="ProcessHandle"/> reads the
     * processor clock off the BCL and returns null for the rest, so the processor is the whole of
     * it. Registered by the module itself and stepped aside for by every platform module – this is
     * a statement about this assembly's own fallback process, not about anybody's operating system.
     */
    public sealed class DefaultProcessMetricSupport : IProcessMetricSupport
    {
        public ProcessMetric SupportedMetrics => ProcessMetric.Processor;
    }
}
