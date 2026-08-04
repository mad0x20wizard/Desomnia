using Microsoft.Diagnostics.Tracing.Parsers;

namespace MadWizard.Desomnia.Processes.Manager
{
    /**
     * A per-process metric that rides the process manager's kernel trace session. The manager owns
     * the session and its lifecycle but knows the metrics only through this interface: it never
     * asks one anything back. A metric declares which kernel events it needs, reads them off the
     * shared source, and books what it measures straight into the process objects it finds behind
     * the events' pids – so a new measurement is a new registration in the container, not another
     * change to the manager, and not a new question the manager has to route.
     *
     * Resolved as a collection from the persistent container (where the manager lives), and
     * registered by configuration: an empty collection is a session that traces nothing but
     * process lifetime, exactly as before any metric existed. Note that the persistent container
     * has no PriorityCollectionSource – the collection comes in registration order, and
     * WithPriority does not apply here. Nothing the manager does with it is order-sensitive.
     */
    public interface ITraceEventMetric
    {
        /// <summary>The kernel keywords this metric needs – OR-ed into the session's enable flags.</summary>
        KernelTraceEventParser.Keywords Keywords { get; }

        /// <summary> Subscribes the metric's handlers. Called once per session, before processing starts.</summary>
        void Subscribe(KernelTraceEventParser kernel);

        /// <summary>The session stopped. Numbers nobody maintains must not linger where they could be read as current.</summary>
        void Reset();
    }
}
