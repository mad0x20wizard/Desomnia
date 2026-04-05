namespace MadWizard.Desomnia.Process.Manager
{
    internal class ProcessWrapper(System.Diagnostics.Process process) : IProcess
    {
        internal ProcessWrapper(int id) : this(System.Diagnostics.Process.GetProcessById(id)) { }

        public int Id { get; private init; } = process.Id;
        public int SessionId { get; private init; } = process.SessionId;
        public string Name { get; private init; } = process.ProcessName;

        public System.Diagnostics.Process NativeProcess => process;

        public required IProcess? Parent { get; internal init; }

        public async Task Stop(TimeSpan timeout = default)
        {
            // try gracefull shutdown
            process.CloseMainWindow();

            if (timeout.TotalMilliseconds > 0)
            {
                try
                {
                    await process.WaitForExitAsync(new CancellationTokenSource((int)timeout.TotalMilliseconds).Token);
                }
                catch (TimeoutException)
                {
                    // too late
                }
            }

            if (!process.HasExited)
            {
                process.Kill(); // kill it eventually
            }
        }
    }
}
