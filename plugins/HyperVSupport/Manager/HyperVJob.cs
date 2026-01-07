using Microsoft.Management.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.Network.HyperV.Manager
{
    internal class HyperVJob(HyperVManager manager, CimInstance jobRef)
    {
        static readonly TimeSpan POLL_INTERVAL = TimeSpan.FromMilliseconds(250);

        public string? InstanceID => jobRef.CimInstanceProperties["InstanceID"]?.Value as string;

        public async Task WaitForCompletion(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var deadline = DateTime.Now + (timeout ?? TimeSpan.FromMinutes(2));

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Refresh the job instance using its keys (InstanceID, etc.)
                var job = manager.Session.GetInstance(HyperVManager.NS, jobRef);

                JobState state = (JobState)(ushort)(job.CimInstanceProperties["JobState"].Value ?? (ushort)0);

                switch (state)
                {
                    case JobState.New:
                    case JobState.Starting:
                    case JobState.Running:
                        if (DateTime.Now >= deadline)
                            throw new TimeoutException($"Timed out waiting for HyperV job (InstanceID={InstanceID}) to complete.");

                        await Task.Delay(POLL_INTERVAL, cancellationToken);

                        continue;

                    case JobState.Completed:
                    case JobState.CompletedWithWarnings:
                        return; // Completed

                    case JobState.Killed:
                    case JobState.Terminated:
                    case JobState.Exception:
                        throw CreateError(job, state);

                    default:
                        throw new InvalidOperationException($"Unknown job state: {state} (InstanceID={InstanceID})");
                }
            }
        }

        private Exception CreateError(CimInstance job, JobState state)
        {
            // Try to provide rich error context
            string? errDesc = job.CimInstanceProperties["ErrorDescription"]?.Value as string;
            uint? errCode = job.CimInstanceProperties["ErrorCode"]?.Value as uint?;

            // Some providers implement GetError() -> CIM_Error; fetch if available
            try
            {
                var res = manager.Session.InvokeMethod(HyperVManager.NS, job, "GetError", new CimMethodParametersCollection());
                var cimErr = res?.OutParameters?["Error"]?.Value as CimInstance;
                if (cimErr != null)
                {
                    var msg = cimErr.CimInstanceProperties["Message"]?.Value as string;
                    if (!string.IsNullOrWhiteSpace(msg)) errDesc = msg;
                }
            }
            catch { /* not all providers support GetError */ }

            return new InvalidOperationException(
                $"CIM job ended in state {state} (InstanceID={InstanceID}). " +
                $"ErrorCode={errCode?.ToString() ?? "n/a"}, ErrorDescription='{errDesc ?? "n/a"}'.");
        }
    }

    internal enum JobState : ushort
    {
        New = 2,
        Starting = 3,
        Running = 4,
        Suspended = 5,
        ShuttingDown = 6,
        Completed = 7,
        Terminated = 8,
        Killed = 9,
        Exception = 10,

        CompletedWithWarnings = 32768
    }
}
