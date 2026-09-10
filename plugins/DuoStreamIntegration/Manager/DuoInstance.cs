using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Watch;
using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Service.Duo.Sunshine;
using MadWizard.Desomnia.Session;
using MadWizard.Desomnia.Session.Manager;
using Microsoft.Win32;

namespace MadWizard.Desomnia.Service.Duo.Manager
{
    public class DuoInstance : ResourceMonitor<Resource>
    {
        private int _runningState = -1;

        internal readonly SemaphoreSlim CommandSemaphore = new(1, 1);

        public DuoInstance(DuoInstanceInfo info, InstanceSettings settings, RegistryKey? key = null)
        {
            Key         = key;
            Info        = info;
            Watch       = info.Watch;
            Settings    = settings;
            Service     = new SunshineService(Name, Port);

            Event(nameof(Demand)).AddAction(info.OnDemand);
            Event(nameof(Idle)).AddAction(info.OnIdle);

            Started.AddAction(info.OnStart);
            Stopped.AddAction(info.OnStop);
        }

        private RegistryKey? Key { get; }

        internal DuoInstanceInfo Info { get; }
        internal WatchExpression Watch { get; set; }

        private InstanceSettings Settings { get; }

        public string Name => Settings.Name;
        public ushort Port => Settings.Port;
        public string UserName => Settings.UserName;
        public bool IsSandboxed => Settings.IsSandboxed;

        public SunshineService Service { get; }

        public uint? SessionID
        {
            get
            {
                return Key != null ? (uint?)(Key["SessionId"] as int?) : field;
            }

            set
            {
                Key?["SessionId"] = value;

                field = value;
            }
        }

        public bool IsBusy => CommandSemaphore.CurrentCount == 0;

        public bool? IsRunning => Volatile.Read(ref _runningState) switch
        {
            < 0 => null,
              0 => false,
            > 0 => true,
        };

        [EventContext]
        public ISession? Session { get; internal set; }

        public event EventInvocation? Started;
        public event EventInvocation? Stopped;

        internal event Action<bool>? RunningStateChanged;

        internal bool SetRunningState(bool? value)
        {
            var state = value switch
            {
                null    => -1,
                false   => 0,
                true    => 1,
            };
            var previous = Interlocked.Exchange(ref _runningState, state);

            if (previous == state || previous < 0 || value is not bool running)
                return false;

            RunningStateChanged?.Invoke(running);
            return true;
        }

        internal Task TriggerRunningStateChangedAsync(bool running)
        {
            return running ? Started.TriggerEventAsync() : Stopped.TriggerEventAsync();
        }

        internal StateObservation ObserveState(bool running) => new(this, running);

        internal sealed class StateObservation : IDisposable
        {
            private readonly DuoInstance _instance;
            private readonly bool _running;
            private readonly TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public StateObservation(DuoInstance instance, bool running)
            {
                _instance = instance;
                _running = running;

                // Subscribe before reading: a transition during subscription must not be lost.
                instance.RunningStateChanged += StateChanged;
                if (instance.IsRunning is bool state)
                    StateChanged(state);
            }

            public Task WaitAsync(CancellationToken token) => _changed.Task.WaitAsync(token);

            private void StateChanged(bool running)
            {
                if (running == _running)
                    _changed.TrySetResult();
            }

            public void Dispose() => _instance.RunningStateChanged -= StateChanged;
        }

        public bool HasInitiated(ISession session)
        {
            return this.Name == session.ClientName && this.UserName == session.UserName;
        }

        internal async Task NetworkServiceWatch_Demand(Event @event)
        {
            if (@event is not InspectionEvent) // don't trigger for inspection events
            {
                await TriggerDemandAsync();
            }
        }

        protected override bool ShouldTriggerEvent(Event @event)
        {
            if (@event.Type == nameof(Idle) && IsRunning != true)
                return false; // only trigger "Idle" events if the instance is running

            if (@event.Type == nameof(Demand) && (IsRunning == true || @event is InspectionEvent))
                return false; // only trigger "Demand" events if the instance is NOT running

            return base.ShouldTriggerEvent(@event);
        }

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            var tokens = base.InspectResource(interval).ToArray();

            if (tokens.OfType<SessionUsage>().FirstOrDefault() is { } session)
            {
                var duo = new DuoSessionUsage(Name, UserName) { Metrics = session.Metrics };

                foreach (var t in session.Tokens)
                {
                    duo.Tokens.Add(t);
                }

                if (this.OfType<NetworkServiceWatch>().Any())
                {
                    var network = tokens.OfType<NetworkServiceUsage>().FirstOrDefault();

                    if (network is not null)
                    {
                        network.Name = "Sunshine";

                        duo.Tokens.Add(network);
                    }

                    duo.Metrics.Add("StreamTraffic", network is not null);
                }

                if (Watch.Evaluate(duo.Metrics) || session.Tokens.Count > 0)
                {
                    yield return duo;
                }
            }
        }

        protected override void HandleInspectionResult(TimeSpan duration, IEnumerable<UsageToken> tokens)
        {
            base.HandleInspectionResult(duration, tokens);

            // sync idle/demand state with SessionWatch
            this.OfType<SessionWatch>().SingleOrDefault()?.InjectInspectionResult(duration, tokens);
        }

        public override void Dispose()
        {
            try
            {
                base.Dispose();
            }
            finally
            {
                Key?.Dispose();
            }
        }

        public sealed record InstanceSettings
        {
            public required string    Name            { get; init; }
            public required ushort    Port            { get; init; }
            public required string    UserName        { get; init; }
            public required bool      IsSandboxed     { get; init; }
        }

        public override string ToString()
        {
            return $"DuoInstance<{Name}>";
        }
    }
}
