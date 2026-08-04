using MadWizard.Desomnia.Processes.Manager;
using System.Diagnostics;

namespace MadWizard.Desomnia.Processes.Tests
{
    /// <summary>
    /// A process that exists as far as anything but the OS is concerned. The two expensive members
    /// are instrumented rather than answered: <see cref="ProcessorTime"/> counts every sample so a
    /// test can prove a cycle took none, and <see cref="ImagePath"/> throws outright, since nothing
    /// here should be matching on paths.
    /// </summary>
    internal sealed class FakeProcess(int id, string name) : IProcess
    {
        public int Id => id;
        public int SessionId => 0;
        public string Name => name;

        public IProcess? Parent { get; init; }

        public bool HasStopped { get; set; }

        /// <summary>The processor time to report; null is a process that can no longer be sampled.</summary>
        public TimeSpan? Cpu { get; set; }

        /// <summary>How often anything asked — the cheapest proof that a cycle did not sample.</summary>
        public int CpuSamples { get; private set; }

        public TimeSpan? ProcessorTime
        {
            get
            {
                CpuSamples++;

                return Cpu;
            }
        }

        /// <summary>The storage counters to report; null is a platform (or moment) that cannot answer.</summary>
        public ProcessInputOutput? Disk { get; set; }

        /// <summary>How often the storage counters were asked for — proof a cycle did (not) sample.</summary>
        public int DiskSamples { get; private set; }

        public ProcessInputOutput? StorageData
        {
            get
            {
                DiskSamples++;

                return Disk;
            }
        }

        /// <summary>The traffic counters to report; null mimics the platforms without a meter.</summary>
        public ProcessInputOutput? Net { get; set; }

        public ProcessInputOutput? NetworkData => Net;

        public string? Path { get; set; }

        public string? ImagePath => Path ?? throw new Xunit.Sdk.XunitException($"'{name}' should not have been asked for its path");

        public Task Stop(TimeSpan timeout = default) => Task.CompletedTask;

        public event EventHandler? Stopped;

        public void RaiseStopped() => Stopped?.Invoke(this, EventArgs.Empty);

        public void Dispose() { } // nothing real to release behind a fake
    }

    /// <summary>A fixed roster of processes instead of a live OS enumeration.</summary>
    internal sealed class FakeProcessSource(params IProcess[] processes) : IProcessManager
    {
        private readonly List<IProcess> _processes = [.. processes];

        public IProcess this[int pid] => _processes.FirstOrDefault(process => process.Id == pid)
            ?? throw new KeyNotFoundException("Process with pid = " + pid + " not found");

        public IProcess LaunchProcess(ProcessStartInfo info) => throw new NotSupportedException();

        public event EventHandler<IProcess>? ProcessStarted;
        public event EventHandler<IProcess>? ProcessStopped;

        public void Start(IProcess process)
        {
            _processes.Add(process);

            ProcessStarted?.Invoke(this, process);
        }

        public void Stop(IProcess process)
        {
            _processes.Remove(process);

            ProcessStopped?.Invoke(this, process);
        }

        public IEnumerator<IProcess> GetEnumerator() => _processes.GetEnumerator();
    }
}
