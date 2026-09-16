using MadWizard.Desomnia.Power.Source;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MadWizard.Desomnia.Power.Manager
{
    // Fallback implementation that uses /sys/power/state directly (no D-Bus / logind required).
    // Power inhibitor locks are not available via sysfs — CreateRequest throws a NotSupportedException.
    // Suspended/ResumeSuspended events are only fired around suspend calls made by this daemon;
    // externally-triggered suspends are not observable without D-Bus.

    public class SysPowerManager : IPowerManager
    {
        // see: https://www.kernel.org/doc/Documentation/power/states.txt
        private const string SysPowerState = "/sys/power/state";

        private const string ShutdownCommand = "shutdown";

        public required ILogger<SysPowerManager> Logger { private get; init; }

        public event EventHandler? Suspended;
        public event EventHandler? ResumeSuspended;

        public PowerSource Source => SysfsPowerSource.Read();

        private static string[] AvailableStates => File.ReadAllText(SysPowerState).Split(' ');

        private string State
        {
            set
            {
                var supported = !AvailableStates.Contains(value)
                    ? throw new NotSupportedException($"Power state {value} is not supported by this system.") 
                    : true;

                Logger.LogDebug("/sys/power/state = '{state}'", value);

                Suspended?.Invoke(this, EventArgs.Empty);
                File.WriteAllText(SysPowerState, value); // blocks until resume
                ResumeSuspended?.Invoke(this, EventArgs.Empty);

                Logger.LogDebug("/sys/power/state = '{state}'", "");
            }
        }

        #region Power state changes
        public async Task Suspend()
        {
            Logger.LogDebug("Requested ACPI state: {acpi}", "S1-S3 (sleep)");

            State = "mem";
        }

        public async Task Hibernate()
        {
            Logger.LogDebug("Requested ACPI state: {acpi}", "S4 (hibernate)");

            State = "disk";
        }

        public async Task Shutdown(TimeSpan? timeout = null, string? message = null, bool force = false)
        {
            Logger.LogDebug("Requested ACPI state: S5 (shutdown)");

            await ExecuteShutdown("-P", timeout, message);
        }

        public async Task Reboot(TimeSpan? timeout = null, string? message = null, bool force = false)
        {
            Logger.LogDebug("Requested ACPI state: S0 (reboot)");

            await ExecuteShutdown("-r", timeout, message);
        }

        private async Task ExecuteShutdown(string flag, TimeSpan? timeout, string? message = null)
        {
            string time = timeout.HasValue // Shutdown(8) uses minutes; round up, minimum 1 minute for any non-zero Delay.
                ? $"+{Math.Max(1, (int)Math.Ceiling(timeout.Value.TotalMinutes))}"
                : "now";

            var startInfo = new ProcessStartInfo(ShutdownCommand) { UseShellExecute = false, CreateNoWindow = true };
            //startInfo.ArgumentList.Add("--no-wall");
            startInfo.ArgumentList.Add(flag);
            startInfo.ArgumentList.Add(time);

            if (message is not null)
            {
                startInfo.ArgumentList.Add($"\"${message}\"");
            }

            using var process = System.Diagnostics.Process.Start(startInfo) 
                ?? throw new Exception($"Failed to start '${ShutdownCommand}' command.");

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                throw new Exception($"'${ShutdownCommand}' exited with code {process.ExitCode}.");
        }
        #endregion

        async Task<IPowerRequest> IPowerManager.CreateRequest(PowerRequestType type, string reason)
        {
            throw new NotSupportedException($"Sleep inhibition is not supported by this system.");
        }

        async IAsyncEnumerator<IPowerRequest> IAsyncEnumerable<IPowerRequest>.GetAsyncEnumerator(CancellationToken token)
        {
            yield break;
        }
    }
}