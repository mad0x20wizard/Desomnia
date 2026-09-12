using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Watch;
using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Service.Duo.Sunshine;
using MadWizard.Desomnia.Session;
using MadWizard.Desomnia.Session.Manager;
using Nito.AsyncEx;

namespace MadWizard.Desomnia.Service.Duo
{
    public class DuoInstance : ResourceMonitor<Resource>
    {
        private int _runningState = -1;

        internal AsyncLock Mutex { get; } = new();

        public DuoInstance(string name, InstanceSettings settings, DuoInstanceWatchInfo info)
        {
            Name        = name;
            Settings    = settings;
            Service     = new SunshineService(Name, Settings.Port);
            Info        = info;

            Event(nameof(Demand)).AddAction(info.OnDemand);
            Event(nameof(Idle)).AddAction(info.OnIdle);

            Started.AddAction(info.OnStart);
            Stopped.AddAction(info.OnStop);
        }

        public string Name { get; }

        internal InstanceSettings Settings { get; }
        internal SunshineService Service { get; }

        internal DuoInstanceWatchInfo Info { get; }
        internal WatchExpression? Watch { get; set; }

        public bool? IsRunning
        {
            get
            {
                return Volatile.Read(ref _runningState) switch
                {
                    < 0 => false,
                      0 => null,
                    > 0 => true,
                };
            }

            internal set
            {
                var state = value switch
                {
                    false   => -1,
                    null    =>  0,
                    true    => +1,
                };

                Interlocked.Exchange(ref _runningState, state);
            }
        }

        [EventContext]
        public ISession? Session => this.OfType<SessionWatch>().FirstOrDefault()?.Session;

        public event EventInvocation? Started;
        public event EventInvocation? Stopped;

        #region Idle / Demand detection
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
                var duo = new DuoSessionUsage(Name, session.UserName) { Metrics = session.Metrics };

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

                if (Watch?.Evaluate(duo.Metrics) ?? false || session.Tokens.Count > 0)
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
        #endregion

        public override void Dispose()
        {
            base.Dispose();
        }

        public override string ToString()
        {
            return $"DuoInstance<{Name}>";
        }
    }
}
