using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Service.Duo.Sunshine;
using MadWizard.Desomnia.Session;
using MadWizard.Desomnia.Session.Manager;
using Microsoft.Win32;
using Nito.AsyncEx;

namespace MadWizard.Desomnia.Service.Duo.Manager
{
    public class DuoInstance : ResourceMonitor<Resource>
    {
        private readonly RegistryKey Key;

        internal readonly SemaphoreSlim Semaphore = new(1, 1);
        internal readonly AsyncLock RefreshMutex = new();

        public DuoInstance(DuoInstanceInfo info, RegistryKey key)
        {
            Info = info;

            GetEvent(nameof(Demand)).AddAction(info.OnDemand);
            GetEvent(nameof(Idle)).AddAction(info.OnIdle);

            Started.AddAction(info.OnStart);
            Stopped.AddAction(info.OnStop);

            Key = key;

            Name = Key.Name.Split('\\').Last();
            Port = Key.GetValue("Port") is int port ? (ushort)port : throw new ArgumentNullException("Port");
            UserName = Key.GetValue("UserName") is string name ? name : throw new ArgumentNullException("UserName");
            IsSandboxed = Key.GetValue("Sandboxed") is int sandboxed ? sandboxed == 1 : false;

            Service = new SunshineService(Name, Port);
        }

        internal DuoInstanceInfo Info { get; private init; }

        public string Name      { get; private set; }
        public ushort Port      { get; private set; }
        public string UserName  { get; private set; }

        public bool IsSandboxed { get; private set; }

        public SunshineService Service { get; private set; }

        public uint? SessionID
        {
            get
            {
                return (uint?)(Key.GetValue("SessionId") as int?);
            }

            set
            {
                if (value != null)
                    Key.SetValue("SessionId", value);
                else if (Key.GetValue("SessionId") != null)
                    try { Key.DeleteValue("SessionId"); } catch (ArgumentException) { /* No value exists with that name. */ }
            }
        }

        public bool IsBusy => Semaphore.CurrentCount == 0;

        public bool? IsRunning
        {
            get;

            internal set
            {
                if (field != value)
                {
                    if (field != null)
                    {
                        if (value == true)
                            Started.TriggerEvent();
                        else if (value == false)
                            Stopped.TriggerEvent();
                    }
                }

                field = value;
            }
        }

        [EventContext]
        public ISession? Session { get; internal set; }

        public event EventInvocation? Started;
        public event EventInvocation? Stopped;

        internal event EventHandler<TimeSpan>? Inspected;

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

        protected override bool OnEventTriggering(Event @event)
        {
            if (@event.Type == nameof(Idle) && IsRunning != true)
                return false; // only trigger "Idle" events if the instance is running

            if (@event.Type == nameof(Demand) && (IsRunning == true || @event is InspectionEvent))
                return false; // only trigger "Demand" events if the instance is NOT running

            return base.OnEventTriggering(@event);
        }

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            Inspected?.Invoke(this, interval);

            var tokens = base.InspectResource(interval).ToArray();

            if (tokens.Length > 0)
            {
                var duo = new DuoSessionUsage(Name, UserName);

                foreach (var token in tokens)
                {
                    switch (token)
                    {
                        case SessionUsage session:
                            duo.LastInputTime = session.LastInputTime;
                            duo.Metrics = session.Metrics;
                            foreach (var t in session.Tokens)
                                duo.Tokens.Add(t);
                            break;

                        case NetworkServiceUsage network:
                            network.Name = "Sunshine";
                            duo.Tokens.Add(network);
                            break;
                    }
                }

                yield return duo;
            }
        }

        public override void Dispose()
        {
            Key?.Dispose();

            base.Dispose();
        }

        public override string ToString()
        {
            return $"DuoInstance<{Name}>";
        }
    }
}
