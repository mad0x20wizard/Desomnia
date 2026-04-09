using NativeProcess = System.Diagnostics.Process;

namespace MadWizard.Desomnia.Process.Manager
{
    internal class ProcessWrapper : IProcess
    {
        public NativeProcess NativeProcess { get; init; }

        internal ProcessWrapper(int id) : this(NativeProcess.GetProcessById(id)) { }

        internal ProcessWrapper(NativeProcess process)
        {
            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += TriggerStop;
            }
            catch (Exception)
            {
                // may not be possible
            }

            NativeProcess = process;

            Id = process.Id;
            SessionId = process.SessionId;
            Name = process.ProcessName;
        }

        public int Id { get; private init; }
        public int SessionId { get; private init; }
        public string Name { get; private init; }

        public required IProcess? Parent { get; internal init; }

        public bool HasStopped => NativeProcess.HasExited;

        public virtual async Task Stop(TimeSpan timeout = default)
        {
            // try gracefull shutdown
            NativeProcess.CloseMainWindow();

            if (timeout.TotalMilliseconds > 0)
            {
                try
                {
                    await NativeProcess.WaitForExitAsync(new CancellationTokenSource((int)timeout.TotalMilliseconds).Token);
                }
                catch (TimeoutException)
                {
                    // too late
                }
            }

            if (!NativeProcess.HasExited)
            {
                NativeProcess.Kill(); // kill it eventually
            }
        }

        public event EventHandler? Stopped;

        internal void TriggerStop(object? sender = null, EventArgs? e = null)
        {
            Stopped?.Invoke(this, EventArgs.Empty);

            Stopped = null;
        }
    }
}
