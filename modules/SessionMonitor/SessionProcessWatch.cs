using MadWizard.Desomnia.Process;
using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Session
{
    public class SessionProcessWatch : ProcessWatch
    {
        [EventContext]
        public required ISession Session
        {
            get;
            init
            {
                field = value;

                field.Connected += Session_Connected;
                field.Disconnected += Session_Disconnected;
            }
        }

        public event EventInvocation? SessionIdle;
        public event EventInvocation? SessionDemand;
        public event EventInvocation? SessionConsoleConnected;
        public event EventInvocation? SessionRemoteConnected;
        public event EventInvocation? SessionDisconnected;

        public SessionProcessWatch(SessionProcessWatchInfo info) : base(info)
        {
            AddEventAction(nameof(SessionIdle), info.OnSessionIdle);
            AddEventAction(nameof(SessionDemand), info.OnSessionDemand);
            AddEventAction(nameof(SessionConsoleConnected), info.OnSessionConsoleConnect);
            AddEventAction(nameof(SessionRemoteConnected), info.OnSessionRemoteConnect);
            AddEventAction(nameof(SessionDisconnected), info.OnSessionDisconnect);
        }

        #region SessionWatch events
        protected override void StartTrackingBy(ResourceMonitor monitor, bool adopt)
        {
            base.StartTrackingBy(monitor, adopt);

            monitor.Idle += SessionWatch_Idle;
            monitor.Demand += SessionWatch_Demand;
        }

        private async Task SessionWatch_Idle(Event data)
        {
            CancelEventAction(nameof(SessionDemand));

            TriggerEvent(nameof(SessionIdle));
        }

        private async Task SessionWatch_Demand(Event data)
        {
            CancelEventAction(nameof(SessionIdle));

            TriggerEvent(nameof(SessionDemand));
        }

        protected override void StopTrackingBy(ResourceMonitor monitor)
        {
            monitor.Demand -= SessionWatch_Demand;
            monitor.Idle -= SessionWatch_Idle;

            base.StopTrackingBy(monitor);
        }
        #endregion

        #region Session events
        private void Session_Connected(object? sender, EventArgs e)
        {
            if (Session.IsConnected)
            {
                CancelEventAction(nameof(SessionDisconnected));

                if (Session.IsConsoleConnected)
                    TriggerEvent(nameof(SessionConsoleConnected));
                if (Session.IsRemoteConnected)
                    TriggerEvent(nameof(SessionRemoteConnected));
            }
        }

        private void Session_Disconnected(object? sender, EventArgs e)
        {
            CancelEventAction(nameof(SessionConsoleConnected));
            CancelEventAction(nameof(SessionRemoteConnected));

            TriggerEvent(nameof(SessionDisconnected));
        }
        #endregion

        public override void Dispose()
        {
            Session.Disconnected -= Session_Disconnected;
            Session.Connected -= Session_Connected;

            base.Dispose();
        }
    }
}
