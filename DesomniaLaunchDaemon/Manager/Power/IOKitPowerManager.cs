using MadWizard.Desomnia.LaunchDaemon.Native;
using MadWizard.Desomnia.Power.Source;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace MadWizard.Desomnia.Power.Manager
{
    /// <summary>
    /// macOS implementation of <see cref="IPowerManager"/>, built on IOKit power management
    /// (IOPMLib) — daemon-safe, validated by probes/PowerProbe.MacOS:
    ///
    ///  - Power requests are IOPM assertions. The default type (PreventSystemSleep) also holds
    ///    the system up from a dark wake — the state a demand-triggered network wake lands in.
    ///  - Suspend() forces software sleep via IOPMSleepSystem, overriding ALL assertions
    ///    (the same semantics as logind's SD_LOGIND_SKIP_INHIBITORS on Linux).
    ///  - Hibernate() has no direct API either: it temporarily switches pmset's hibernatemode
    ///    to 25 (suspend-to-disk) for one sleep and reverts it after the wake-up.
    ///  - Apple Silicon has no deep sleep: the machine dark-wakes for network traffic without
    ///    any notification here. Suspended/ResumeSuspended reflect user-visible (full) sleep
    ///    and wake only; dark wakes are intentionally invisible.
    ///
    /// It is also the platform's <see cref="IPowerSource"/>: it lives in the persistent
    /// container (machine lifetime), so the same instance can back the "power" environment
    /// condition of every configuration rebuild — one IOKit notification thread instead of two.
    ///
    /// Sleep/wake and power-source notifications run on a dedicated CFRunLoop thread
    /// (see <see cref="RunLoopThread"/>; IHostedService lifecycle).
    /// </summary>
    public partial class IOKitPowerManager : RunLoopThread, IPowerManager, IPowerSource, IHostedService
    {
        const string AssertionName = "Desomnia Sleep Management";

        /// <summary>Assertion types that count as power requests when enumerating; everything else is plumbing.</summary>
        private static readonly string[] MonitoredAssertionTypes =
        [
            nameof(SleepAssertion.PreventUserIdleSystemSleep),
            nameof(SleepAssertion.PreventSystemSleep),
            nameof(SleepAssertion.NetworkClientActive),
            IOPM.kIOPMAssertionTypePreventUserIdleDisplaySleep,
        ];

        public required ILogger<IOKitPowerManager> Logger { private get; init; }

        public event EventHandler? Suspended;
        public event EventHandler? ResumeSuspended;

        #region Power-Source
        readonly Lock _sourceLock = new();

        EventHandler? _sourceChanged;

        PowerSource _lastSource;

        public PowerSource Source
        {
            get
            {
                nint snapshot = IOPM.IOPSCopyPowerSourcesInfo();

                if (snapshot == 0)
                    return PowerSource.Unknown;

                try
                {
                    return CF.ToManagedString(IOPM.IOPSGetProvidingPowerSourceType(snapshot)) switch
                    {
                        "AC Power" => PowerSource.AC,
                        "Battery Power" => PowerSource.Battery,
                        "UPS Power" => PowerSource.Battery, // wall power is gone

                        _ => PowerSource.Unknown,
                    };
                }
                finally
                {
                    CF.CFRelease(snapshot);
                }
            }
        }

        /// <summary>
        /// Change notifications from IOPSNotificationCreateRunLoopSource, on this manager's
        /// run loop (registered in <see cref="Initialize"/>). The loop is spawned by the first
        /// subscriber as well as by the hosted-service start — the environment conditions
        /// subscribe while the persistent host is still being built, before that start.
        /// </summary>
        public event EventHandler? SourceChanged
        {
            add
            {
                lock (_sourceLock)
                {
                    if (_sourceChanged is null && value is not null)
                    {
                        _lastSource = Source;

                        EnsureStarted(); // no-op once the run loop is up
                    }

                    _sourceChanged += value;
                }
            }
            remove
            {
                // the run loop keeps running; without subscribers the callback is a cheap no-op
                lock (_sourceLock)
                {
                    _sourceChanged -= value;
                }
            }
        }

        [UnmanagedCallersOnly]
        private static void OnPowerSourcesChanged(nint refCon)
            => Self<IOKitPowerManager>(refCon).OnPowerSourceMessage();

        private void OnPowerSourceMessage()
        {
            EventHandler? handler;

            lock (_sourceLock)
            {
                var current = Source;

                if (current == _lastSource)
                    return; // battery percentage and the like fire this too

                Logger.LogDebug("Power source changed: {source}", current);

                _lastSource = current;

                handler = _sourceChanged;
            }

            handler?.Invoke(this, EventArgs.Empty);
        }
        #endregion

        #region Power-Requests
        async Task<IPowerRequest> IPowerManager.CreateRequest(PowerRequestType type, string reason)
        {
            string assertionType = type switch
            {
                PowerRequestType.Display => IOPM.kIOPMAssertionTypePreventUserIdleDisplaySleep,

                _ => IOPM.kIOPMAssertionTypePreventSystemSleep
            };

            var request = new AssertionRequest(AssertionName, reason, assertionType)
            {
                PID = Environment.ProcessId,
                AssertionId = IOPM.CreateAssertion(assertionType, AssertionName, reason),
            };

            Logger.LogTrace("Created {request}", request);

            return request;
        }

        async IAsyncEnumerator<IPowerRequest> IAsyncEnumerable<IPowerRequest>.GetAsyncEnumerator(CancellationToken token)
        {
            int rc = IOPM.IOPMCopyAssertionsByProcess(out nint assertionsByPid);

            if (rc != 0)
                throw new Exception($"IOPMCopyAssertionsByProcess failed (0x{rc:X8})");

            if (assertionsByPid == 0)
                yield break; // no process holds any assertion

            try
            {
                var (pids, assertionArrays) = CF.GetKeysAndValues(assertionsByPid);

                for (int i = 0; i < pids.Length; i++)
                {
                    if (!CF.IsArray(assertionArrays[i]))
                        continue;

                    long pid = CF.ToNumber(pids[i]) ?? 0;

                    for (nint j = 0; j < CF.CFArrayGetCount(assertionArrays[i]); j++)
                    {
                        nint assertion = CF.CFArrayGetValueAtIndex(assertionArrays[i], j);

                        if (CF.GetString(assertion, IOPM.kIOPMAssertionTypeKey) is not string type || !MonitoredAssertionTypes.Contains(type))
                            continue;

                        string name = CF.GetString(assertion, IOPM.kIOPMAssertionNameKey) ?? "?";

                        string? reason = CF.GetString(assertion, IOPM.kIOPMAssertionDetailsKey)
                                      ?? CF.GetString(assertion, IOPM.kIOPMAssertionHumanReadableReasonKey);

                        yield return new AssertionRequest(name, reason, type)
                        {
                            PID = pid,
                            ProcessName = CF.GetString(assertion, IOPM.kIOPMAssertionProcessNameKey),
                        };
                    }
                }
            }
            finally
            {
                CF.CFRelease(assertionsByPid);
            }
        }
        #endregion

        #region Power Transitions
        public async Task Suspend()
        {
            const string acpi = "S1-S3 (sleep)";

            if (IOPM.IOPMSleepEnabled() != 0)
            {
                Logger.LogDebug("Requested ACPI state: {acpi}", acpi);

                SleepNow();
            }
            else
            {
                Logger.LogWarning("Requested ACPI state: {acpi} [unsupported]", acpi);
            }
        }

        public async Task Hibernate()
        {
            const string acpi = "S4 (hibernate)";

            // macOS has no "hibernate now" API — hibernation is a sleep *mode*. Temporarily
            // switch to suspend-to-disk for this one sleep and revert after the wake-up.
            if (IOPM.IOPMSleepEnabled() != 0 && await ReadHibernateMode() is int mode)
            {
                Logger.LogDebug("Requested ACPI state: {acpi}", acpi);

                if (mode != HIBERNATE_MODE_SUSPEND_TO_DISK)
                {
                    await WriteHibernateMode(HIBERNATE_MODE_SUSPEND_TO_DISK);

                    lock (_hibernateLock)
                        _restoreHibernateMode = mode; // reverted on SystemHasPoweredOn
                }

                try
                {
                    SleepNow();
                }
                catch
                {
                    await RestoreHibernateMode();

                    throw;
                }
            }
            else
            {
                Logger.LogWarning("Requested ACPI state: {acpi} [unsupported]", acpi);
            }
        }

        private static void SleepNow()
        {
            uint fb = IOPM.IOPMFindPowerManagement(0);

            if (fb == 0)
                throw new Exception("IOPMFindPowerManagement failed");

            try
            {
                // forced sleep — overrides all assertions (probe-verified)
                int rc = IOPM.IOPMSleepSystem(fb);

                if (rc != 0)
                    throw new Exception($"IOPMSleepSystem failed (0x{rc:X8})");
            }
            finally
            {
                IOKit.IOServiceClose(fb);
            }
        }

        public async Task Shutdown(TimeSpan? timeout = null, string? message = null, bool force = false)
        {
            Logger.LogDebug("Requested ACPI state: {acpi}", "S5 (shutdown)");

            if (message != null)
                Logger.LogInformation("Shutdown message: {message}", message);

            await ExecuteShutdown("-h", timeout, message);
        }

        public async Task Reboot(TimeSpan? timeout = null, string? message = null, bool force = false)
        {
            Logger.LogDebug("Requested ACPI state: {acpi}", "S0 (reboot)");

            if (message != null)
                Logger.LogInformation("Reboot message: {message}", message);

            await ExecuteShutdown("-r", timeout, message);
        }

        private static async Task ExecuteShutdown(string flag, TimeSpan? timeout, string? message)
        {
            const string ShutdownCommand = "/sbin/shutdown";

            string time = timeout.HasValue // shutdown(8) uses minutes; round up, minimum 1 minute for any non-zero delay
                ? $"+{Math.Max(1, (int)Math.Ceiling(timeout.Value.TotalMinutes))}"
                : "now";

            var startInfo = new System.Diagnostics.ProcessStartInfo(ShutdownCommand) { UseShellExecute = false, CreateNoWindow = true };
            startInfo.ArgumentList.Add(flag);
            startInfo.ArgumentList.Add(time);

            if (message is not null)
            {
                startInfo.ArgumentList.Add(message); // broadcast to logged-in users via wall(1)
            }

            using var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new Exception($"Failed to start '{ShutdownCommand}' command.");

            if (!timeout.HasValue)
            {
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                    throw new Exception($"'{ShutdownCommand}' exited with code {process.ExitCode}.");
            }
            else
            {
                // with a future time, BSD shutdown(8) stays resident until the deadline —
                // only verify that it didn't reject the invocation right away
                await Task.Delay(TimeSpan.FromMilliseconds(500));

                if (process.HasExited && process.ExitCode != 0)
                    throw new Exception($"'{ShutdownCommand}' exited with code {process.ExitCode}.");
            }
        }
        #endregion

        #region Hibernate-Mode (pmset)
        const string PmsetCommand = "/usr/bin/pmset";

        /// <summary>pmset hibernatemode writing the memory image to disk AND powering off RAM.</summary>
        const int HIBERNATE_MODE_SUSPEND_TO_DISK = 25;

        private readonly object _hibernateLock = new();

        // NOTE: pmset settings are persistent — if the daemon dies between hibernate and wake-up,
        // the mode stays 25 until the next daemon shutdown/wake restores it (or the user does).
        private int? _restoreHibernateMode;

        [GeneratedRegex(@"^\s*hibernatemode\s+(\d+)\s*$", RegexOptions.Multiline)]
        private static partial Regex HibernateModeSetting();

        /// <summary>Reads the currently active hibernatemode; null if the system doesn't expose one.</summary>
        private static async Task<int?> ReadHibernateMode()
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo(PmsetCommand)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };

            startInfo.ArgumentList.Add("-g");

            using var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new Exception($"Failed to start '{PmsetCommand}' command.");

            string output = await process.StandardOutput.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                return null;

            return HibernateModeSetting().Match(output) is { Success: true } match ? int.Parse(match.Groups[1].Value) : null;
        }

        private async Task WriteHibernateMode(int mode)
        {
            Logger.LogDebug("pmset hibernatemode = {mode}", mode);

            var startInfo = new System.Diagnostics.ProcessStartInfo(PmsetCommand) { UseShellExecute = false, CreateNoWindow = true };

            startInfo.ArgumentList.Add("-a"); // NOTE: flattens per-profile (AC/battery) customizations to one value
            startInfo.ArgumentList.Add("hibernatemode");
            startInfo.ArgumentList.Add(mode.ToString());

            using var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new Exception($"Failed to start '{PmsetCommand}' command.");

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                throw new Exception($"'{PmsetCommand}' exited with code {process.ExitCode}.");
        }

        /// <summary>Reverts a pending hibernatemode change (after wake-up, on failure, or at daemon shutdown).</summary>
        private async Task RestoreHibernateMode()
        {
            int mode;

            lock (_hibernateLock)
            {
                if (_restoreHibernateMode is not int pending)
                    return;

                mode = pending;

                _restoreHibernateMode = null;
            }

            try
            {
                await WriteHibernateMode(mode);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to restore hibernatemode {mode} — check `pmset -g` manually", mode);
            }
        }
        #endregion

        #region Notifications (Sleep/Wake, Power-Source)
        private nint _notifyPort;
        private uint _rootPort;     // io_connect_t used to acknowledge sleep notifications
        private uint _notifier;
        private nint _sourceNotification;

        Task IHostedService.StartAsync(CancellationToken token)
        {
            EnsureStarted(token);

            return Task.CompletedTask;
        }

        protected override unsafe void Initialize()
        {
            _rootPort = IOPM.IORegisterForSystemPower(RefCon, out _notifyPort,
                (nint)(delegate* unmanaged<nint, uint, uint, nint, void>)&OnSystemPowerCallback, out _notifier);

            if (_rootPort == 0)
                throw new Exception("IORegisterForSystemPower failed");

            CF.CFRunLoopAddSource(RunLoop, IOKit.IONotificationPortGetRunLoopSource(_notifyPort), CF.RunLoopDefaultMode);

            Logger.LogTrace("Watching system power notifications: {signal}", "SystemWillSleep, SystemHasPoweredOn");

            // the power-source probe rides on the same loop; its handlers only re-arm a debounce
            // timer, so they cannot hold up a sleep acknowledgment below
            _sourceNotification = IOPM.IOPSNotificationCreateRunLoopSource((nint)(delegate* unmanaged<nint, void>)&OnPowerSourcesChanged, RefCon);

            if (_sourceNotification == 0)
                throw new Exception("IOPSNotificationCreateRunLoopSource failed");

            CF.CFRunLoopAddSource(RunLoop, _sourceNotification, CF.RunLoopDefaultMode);

            Logger.LogTrace("Watching power source notifications.");
        }

        [UnmanagedCallersOnly]
        private static void OnSystemPowerCallback(nint refCon, uint service, uint messageType, nint messageArgument)
            => Self<IOKitPowerManager>(refCon).OnSystemPowerMessage(messageType, messageArgument);

        private void OnSystemPowerMessage(uint messageType, nint messageArgument)
        {
            switch (messageType)
            {
                case IOMessage.kIOMessageCanSystemSleep:
                    // idle sleep negotiation — never veto here; power assertions are the veto mechanism
                    IOPM.IOAllowPowerChange(_rootPort, messageArgument);
                    break;

                case IOMessage.kIOMessageSystemWillSleep:
                    Logger.LogDebug("SystemWillSleep");

                    try
                    {
                        Suspended?.Invoke(this, EventArgs.Empty);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error while handling Suspended event");
                    }
                    finally
                    {
                        // mandatory acknowledgment — without it the kernel stalls the sleep for up to 30s
                        IOPM.IOAllowPowerChange(_rootPort, messageArgument);
                    }
                    break;

                case IOMessage.kIOMessageSystemHasPoweredOn:
                    Logger.LogDebug("SystemHasPoweredOn");

                    _ = RestoreHibernateMode(); // revert a pending hibernatemode change (no-op otherwise)

                    try
                    {
                        ResumeSuspended?.Invoke(this, EventArgs.Empty);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error while handling ResumeSuspended event");
                    }
                    break;
            }
        }

        Task IHostedService.StopAsync(CancellationToken token)
        {
            StopWatching();

            return Task.CompletedTask;
        }

        private void StopWatching()
        {
            RestoreHibernateMode().GetAwaiter().GetResult(); // never leave a modified hibernatemode behind

            Stop();
        }

        protected override void Cleanup()
        {
            if (_notifier != 0)
            {
                IOPM.IODeregisterForSystemPower(ref _notifier);

                _notifier = 0;
            }

            if (_rootPort != 0)
            {
                IOKit.IOServiceClose(_rootPort);

                _rootPort = 0;
            }

            if (_notifyPort != 0)
            {
                IOKit.IONotificationPortDestroy(_notifyPort);

                _notifyPort = 0;
            }

            if (_sourceNotification != 0)
            {
                CF.CFRelease(_sourceNotification);

                _sourceNotification = 0;
            }

            Logger.LogTrace("Stopped watching.");
        }

        public override void Dispose() => StopWatching();
        #endregion
    }
}
