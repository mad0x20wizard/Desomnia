using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Service.Duo.Sunshine;
using MadWizard.Desomnia.Session.Manager;
using Microsoft.Win32;

namespace MadWizard.Desomnia.Service.Duo.Manager
{
    public class DuoInstance : ResourceMonitor<SunshineServiceWatch>
    {
        private bool? _running = null;
        private ISession? _session = null;

        private readonly RegistryKey Key;

        internal readonly SemaphoreSlim Semaphore = new(1, 1);

        public DuoInstance(DuoInstanceInfo info, RegistryKey key)
        {
            AddEventAction(nameof(Demand), info.OnDemand);
            AddEventAction(nameof(Idle), info.OnIdle);

            AddEventAction(nameof(Login), info.OnLogin);
            AddEventAction(nameof(Started), info.OnStart);
            AddEventAction(nameof(Stopped), info.OnStop);
            AddEventAction(nameof(Logoff), info.OnLogoff);

            Key = key;
        }

        public string Name      => Key.Name.Split('\\').Last();
        public ushort Port      => Key.GetValue("Port") is int port ? (ushort)port : throw new ArgumentNullException("Port");
        public string UserName  => Key.GetValue("UserName") is string name ? name : throw new ArgumentNullException("UserName");

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
                    Key.DeleteValue("SessionId");
            }
        }

        public bool IsBusy => Semaphore.CurrentCount == 0;

        public bool IsSandboxed => Key.GetValue("Sandboxed") is int sandboxed ? sandboxed == 1 : false;

        public bool? IsRunning
        {
            get => _running;

            internal set
            {
                if (_running != value)
                {
                    if (_running != null)
                    {
                        if (value == true)
                            TriggerEvent(nameof(Started));
                        else if (value == false)
                            TriggerEvent(nameof(Stopped));
                    }
                }

                _running = value;
            }
        }

        [EventContext]
        public ISession? Session
        {
            get => _session;

            internal set
            {
                _session = value;

                if (value == null)
                    TriggerEvent(nameof(Logoff));
                else if (value != null)
                    TriggerEvent(nameof(Login));
            }
        }

        public event EventInvocation? Login;
        public event EventInvocation? Started;
        public event EventInvocation? Stopped;
        public event EventInvocation? Logoff;

        public bool HasInitiated(ISession session)
        {
            return this.Name == session.ClientName && this.UserName == session.UserName;
        }

        public override bool StartTracking(SunshineServiceWatch service, bool adopt = true)
        {
            service.Demand += NetworkService_Demand;

            return base.StartTracking(service, adopt);
        }

        private async Task NetworkService_Demand(Event demand)
        {
            await TriggerDemandAsync();
        }

        protected override Task TriggerEventAsync(Event @event)
        {
            if (@event.Type == nameof(Idle) && IsRunning != true)
                return Task.CompletedTask; // only trigger "Idle" events if the instance is running
            if (@event.Type == nameof(Demand) && IsRunning == true)
                return Task.CompletedTask; // only trigger "Demand" events if the instance is NOT running

            return base.TriggerEventAsync(@event); // only trigger "Idle" events if the instance is running
        }

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            if (base.InspectResource(interval).Any())
            {
                yield return new DuoStreamUsage(Name);
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
