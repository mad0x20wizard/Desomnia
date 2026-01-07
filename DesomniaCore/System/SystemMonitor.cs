using Autofac;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Power.Manager;
using MadWizard.Desomnia.Ressource;
using Microsoft.Extensions.Logging;

using Timer = System.Timers.Timer;

namespace MadWizard.Desomnia
{
    public class SystemMonitor(SystemMonitorConfig config, IPowerManager power, ActionManager actionManager) : DynamicResourceMonitor<IInspectable>, IStartable, IDisposable
    {
        public required ILogger<SystemMonitor> Logger { protected get; init; }

        private event EventInvocation? SuspendTimeout;

        public event EventInvocation? Suspend;
        public event EventInvocation? Resume;

        private IPowerRequest? Request { get; set; }

        private DateTime? _sleeplessUntil = null;
        private bool? _sleeplessIfUsage = config.OnDemand?.Name == "sleepless";

        private Timer? _sleeplessTimer;

        public bool Sleepless
        {
            get => _sleeplessUntil != null && _sleeplessUntil > DateTime.Now;

            set => SleeplessUntil = value ? DateTime.MaxValue : null;
        }

        public bool? SleeplessIfUsage
        {
            get => _sleeplessIfUsage;

            set
            {
                _sleeplessIfUsage = value;

                SleeplessChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public DateTime? SleeplessUntil
        {
            get => _sleeplessUntil;

            set
            {
                _sleeplessTimer?.Dispose();
                _sleeplessTimer = null;

                _sleeplessUntil = value;

                SleeplessChanged?.Invoke(this, EventArgs.Empty);

                if (_sleeplessUntil != null && _sleeplessUntil != DateTime.MaxValue)
                {
                    _sleeplessTimer = new Timer(_sleeplessUntil.Value - DateTime.Now);
                    _sleeplessTimer.Elapsed += (sender, args) => SleeplessUntil = null;
                    _sleeplessTimer.AutoReset = false;
                    _sleeplessTimer.Start();
                }
            }
        }

        public event EventHandler? SleeplessChanged;

        public override void Start()
        {
            if (config.OnSuspendTimeout is DelayedAction delayed && !delayed.HasDelay)
                throw new ArgumentException("onSuspendTimeout must have a delay set", nameof(config.OnSuspendTimeout));

            AddEventAction(nameof(Idle), config.OnIdle);
            AddEventAction(nameof(Demand), config.OnDemand);
            AddEventAction(nameof(Suspend), config.OnSuspend);
            AddEventAction(nameof(SuspendTimeout), config.OnSuspendTimeout);
            AddEventAction(nameof(Resume), config.OnResume);

            power.Suspended += PowerManager_Suspended;
            power.ResumeSuspended += PowerManager_ResumeSuspended;

            base.Start();
        }

        private void PowerManager_Suspended(object? sender, EventArgs e)
        {
            CancelEventAction(nameof(Idle));
            CancelEventAction(nameof(SuspendTimeout));
        }

        private void PowerManager_ResumeSuspended(object? sender, EventArgs e)
        {
            TriggerEvent(nameof(Resume));
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
        internal void HandleActionReboot() => power.Reboot();
        [ActionHandler("shutdown")]
        internal void HandleActionShutdown() => power.Shutdown();
        [ActionHandler("sleep")]
        internal void HandleActionSleep()
        {
            TriggerEvent(nameof(Suspend));
            TriggerEvent(nameof(SuspendTimeout));

            power.Suspend();
        }

        [ActionHandler("sleepless")]
        internal void HandleActionSleepless(InspectionEvent eventRef, string? reason = null, bool addTokens = true)
        {
            if (reason == null)
            {
                reason = $"No Standby because: " + (eventRef.Tokens.Any() ? string.Join(", ", eventRef.Tokens) : "?");
            }

            if (SleeplessIfUsage != false || eventRef.Tokens.OfType<SleeplessToken>().Any())
            {
                Request = power.CreateRequest($"{reason}");
            }
        }

        protected override async Task<bool> HandleEventAction(Event eventObj, NamedAction action)
        {
            if (!await base.HandleEventAction(eventObj, action))
            {
                if (await actionManager.TryHandleEventAction(eventObj, action))
                    return true;

                return false;
            }

            return true;
        }

        protected override bool HandleActionError(ActionError error)
        {
            return actionManager.HandleActionError(error);
        }

        private void ClearPowerRequest()
        {
            Request?.Dispose();
            Request = null;
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
