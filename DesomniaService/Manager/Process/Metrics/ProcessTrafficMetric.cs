using Microsoft.Diagnostics.Tracing.Parsers;

namespace MadWizard.Desomnia.Processes.Manager.Metrics
{
    /**
     * The precise per-process network meter: actual TCP and UDP payload bytes, booked straight
     * into the <see cref="ProcessTrafficLayer"/> decoration around each event's process – the
     * same source the Resource Monitor's per-process network column drinks from. Registered only
     * when the persistent configuration asks for it
     * (<c>&lt;?global ProcessManager:watchTraffic="active"?&gt;</c>), which is also when the
     * decoration is; a process the meter has never booked into answers from the passive
     * approximation instead.
     */
    public sealed class ProcessTrafficMetric(ProcessManager manager) : ITraceEventMetric
    {
        public KernelTraceEventParser.Keywords Keywords => KernelTraceEventParser.Keywords.NetworkTCPIP;

        /**
         * The kernel's TCP/UDP events carry the transferred size and – unlike most ETW events,
         * whose header pid is whoever happened to be running – the owning process id in their
         * payload, which is what the parser hands out.
         */
        public void Subscribe(KernelTraceEventParser kernel)
        {
            kernel.TcpIpSend        += data => Book(data.ProcessID, sent: data.size);
            kernel.TcpIpRecv        += data => Book(data.ProcessID, received: data.size);
            kernel.TcpIpSendIPV6    += data => Book(data.ProcessID, sent: data.size);
            kernel.TcpIpRecvIPV6    += data => Book(data.ProcessID, received: data.size);
            kernel.UdpIpSend        += data => Book(data.ProcessID, sent: data.size);
            kernel.UdpIpRecv        += data => Book(data.ProcessID, received: data.size);
            kernel.UdpIpSendIPV6    += data => Book(data.ProcessID, sent: data.size);
            kernel.UdpIpRecvIPV6    += data => Book(data.ProcessID, received: data.size);
        }

        // TryFindProcess, not the indexer: a pid the roster does not hold – a process gone before
        // its bytes were pumped, or racing its own start – must not throw in a kernel-event
        // callback, and must certainly not be materialized for having been seen transferring
        // (which is what the indexer would do, at packet rates, from the pump thread)
        private void Book(int pid, int received = 0, int sent = 0)
        {
            if (manager.TryFindProcess(pid, out IProcess? process))
            {
                process.Layer<ProcessTrafficLayer>().BookTraffic(received, sent);
            }
        }

        /**
         * Un-books rather than merely forgets: a meter nobody runs must not leave its last
         * numbers behind where they would be read as "this process stopped transferring".
         * Un-booked, the processes fall back to the passive approximation – which at least
         * keeps moving – and a later session simply books on top of the totals it left.
         */
        public void Reset()
        {
            foreach (var process in manager.Select(p => p.Layer<ProcessTrafficLayer>()))
            {
                process.StopMetering();
            }
        }
    }
}
