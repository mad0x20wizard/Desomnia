using Autofac;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Power.Manager;
using MadWizard.Desomnia.Ressource;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace MadWizard.Desomnia
{
    public class SystemMonitor(SystemMonitorConfig config, IPowerManager power) : DynamicResourceMonitor<IInspectable>, IStartable, IDisposable
    {
        public required ILogger<SystemMonitor> Logger { protected get; init; }

        private event EventInvocation? SuspendTimeout;

        public event EventInvocation? Suspend;
        public event EventInvocation? Resume;

        private IPowerRequest? Request { get; set; }
        private IPowerRequest? DisplayRequest { get; set; }

        public bool MaySleep => config.OnSuspend?.Command?.Function == "sleep";

        public bool Sleepless
        {
            get => SleeplessUntil != null && SleeplessUntil > DateTime.Now;

            set => SleeplessUntil = value ? DateTime.MaxValue : null;
        }

        public bool? SleeplessOnUsage
        {
            get; set
            {
                field = value;

                SleeplessChanged?.Invoke(this, EventArgs.Empty);
            }
        } = config.OnUsage?.Command?.Function == "sleepless";

        public DateTime? SleeplessUntil
        {
            get; set
            {
                _sleeplessTimer?.Dispose();
                _sleeplessTimer = null;

                field = value;

                SleeplessChanged?.Invoke(this, EventArgs.Empty);

                if (field != null && field != DateTime.MaxValue)
                {
                    _sleeplessTimer = new Timer(field.Value - DateTime.Now);
                    _sleeplessTimer.Elapsed += (sender, args) => SleeplessUntil = null;
                    _sleeplessTimer.AutoReset = false;
                    _sleeplessTimer.Start();
                }
            }
        }

        public event EventHandler? SleeplessChanged;

        private Timer? _sleeplessTimer;

        public override void Start()
        {
            if (config.OnSuspendTimeout is DelayedActionInfo delayed && !delayed.HasDelay)
                throw new ArgumentException("onSuspendTimeout must have a delay set", nameof(config.OnSuspendTimeout));

            Event(nameof(Idle)).AddAction(config.OnIdle);      // inherited from Resource —
            Event(nameof(Usage)).AddAction(config.OnUsage);    // string-keyed by necessity

            Suspend.AddAction(config.OnSuspend);
            SuspendTimeout.AddAction(config.OnSuspendTimeout);
            Resume.AddAction(config.OnResume);

            power.Suspended += PowerManager_Suspended;
            power.ResumeSuspended += PowerManager_ResumeSuspended;

            base.Start();
        }

        private void PowerManager_Suspended(object? sender, EventArgs e)
        {
            Event(nameof(Idle)).CancelActions();            // imperative by design: an OS callback,
            Event(nameof(SuspendTimeout)).CancelActions();  // not a trigger-time relation (§6.5)
        }

        private void PowerManager_ResumeSuspended(object? sender, EventArgs e)
        {
            Resume.TriggerEvent();
        }

        public override IEnumerable<UsageToken> Inspect(TimeSpan interval)
        {
            ClearPowerRequest();

            return base.Inspect(interval);
        }

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            if (Sleepless)
                yield return new SleeplessToken();

            foreach (var token in base.InspectResource(interval))
                yield return token;
        }

        [ActionHandler("reboot")]
        internal async Task HandleActionReboot()    => await power.Reboot();
        [ActionHandler("shutdown")]
        internal async Task HandleActionShutdown()  => await power.Shutdown();

        [ActionHandler("sleep")]
        internal async Task HandleActionSleep()
        {
            Suspend.TriggerEvent();

            try
            {
                SuspendTimeout.TriggerEvent();

                await power.Suspend();
            }
            catch (OperationCanceledException)
            {
                SuspendTimeout.Cancel();
            }
        }

        [ActionHandler("sleepless")]
        internal async Task HandleActionSleepless(InspectionEvent eventRef, string? reason = null, bool addTokens = true)
        {
            if (reason == null)
            {
                reason = eventRef.Tokens.Any() ? string.Join(", ", eventRef.Tokens) : "?";
            }

            if (SleeplessOnUsage != false || eventRef.Tokens.OfType<SleeplessToken>().Any())
            {
                Request = await power.CreateRequest(PowerRequestType.System, $"{reason}");

                if (config.KeepDisplayAwake)
                {
                    try
                    {
                        DisplayRequest = await power.CreateRequest(PowerRequestType.Display, $"{reason}");
                    }
                    catch (Exception ex)
                    {
                        // deliberately warns on every cycle — users should notice and take action
                        Logger.LogWarning(ex, "Failed to keep display awake");
                    }
                }
            }
        }

        // unhandled actions and errors reach the ActionManager through the engine's
        // root fallback now (§6.3) — the forwarding overrides that used to live here
        // were the only path to the root, and a brittle one (spec §7.2)

        private void ClearPowerRequest()
        {
            Request?.Dispose();
            Request = null;

            DisplayRequest?.Dispose();
            DisplayRequest = null;
        }

        public override void Dispose()
        {
            ClearPowerRequest();

            base.Dispose();
        }
    }

    public class SleeplessToken : UsageToken
    {
        public override string ToString() => "Sleepless";
    }
}
