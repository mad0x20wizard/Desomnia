using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Processes.Watch;
using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Session
{
    public class SessionProcessWatch : PatternProcessWatch
    {
        [EventContext]
        public required ISession Session
        {
            get; init
            {
                field = value;

                field.Connected += Session_Connected;
                field.Disconnected += Session_Disconnected;
            }
        }

        [EventOpposite(nameof(SessionUsage))]
        public event EventInvocation? SessionIdle;
        public event EventInvocation? SessionUsage;

        public event EventInvocation? SessionConsoleConnected;
        public event EventInvocation? SessionRemoteConnected;

        [EventOpposite(nameof(SessionConsoleConnected), nameof(SessionRemoteConnected))]
        public event EventInvocation? SessionDisconnected;

        public SessionProcessWatch(SessionProcessWatchInfo info) : base(info)
        {
            SessionIdle.AddAction(info.OnSessionIdle);
            SessionUsage.AddAction(info.OnSessionUsage);
            SessionConsoleConnected.AddAction(info.OnSessionConsoleConnect);
            SessionRemoteConnected.AddAction(info.OnSessionRemoteConnect);
            SessionDisconnected.AddAction(info.OnSessionDisconnect);
        }

        #region SessionWatch events
        protected override void OnAttachedTo(EventMetaObject parent)
        {
            if (parent is SessionWatch monitor)
            {
                monitor.Idle += SessionWatch_Idle;
                monitor.Usage += SessionWatch_Usage;
            }
        }

        private async Task SessionWatch_Idle(Event data)
        {
            await SessionIdle.TriggerEventAsync();
        }

        private async Task SessionWatch_Usage(Event data)
        {
            await SessionUsage.TriggerEventAsync();
        }

        protected override void OnDetachedFrom(EventMetaObject parent)
        {
            if (parent is SessionWatch monitor)
            {
                monitor.Usage -= SessionWatch_Usage;
                monitor.Idle -= SessionWatch_Idle;
            }
        }
        #endregion

        #region Session events
        private void Session_Connected(object? sender, EventArgs e)
        {
            if (Session.IsConsoleConnected)
                SessionConsoleConnected.TriggerEvent();
            if (Session.IsRemoteConnected)
                SessionRemoteConnected.TriggerEvent();
        }

        private void Session_Disconnected(object? sender, EventArgs e)
        {
            SessionDisconnected.TriggerEvent();
        }
        #endregion

        public override void Dispose()
        {
            Session.Disconnected -= Session_Disconnected;
            Session.Connected -= Session_Connected;

            base.Dispose();
        }
    }

    public class AnySessionProcessWatch : AnyProcessWatch;
}
