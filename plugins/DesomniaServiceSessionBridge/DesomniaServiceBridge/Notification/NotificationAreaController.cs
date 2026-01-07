using Autofac;
using MadWizard.Desomnia.Pipe.Config;
using MadWizard.Desomnia.Pipe.Messages;
using MadWizard.Desomnia.Power.Manager;
using MadWizard.Desomnia.Service.Bridge.Minion;
using MadWizard.Desomnia.Session.Manager;
using MadWizard.Desomnia.Session.Manager.Bridged;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.Service.Bridge.Notification
{
    public class NotificationAreaController
        (ISessionManager manager, SystemMonitor system, IPowerManager power) 
        : IStartable,
            ISessionMessageHandler<SleeplessMessage>,
            ISessionMessageHandler<EnterStandbyMessage>,
            ISessionMessageHandler<ConnectToConsoleMessage>,
            ISessionMessageHandler<DisconnectMessage>
    {
        public required ILogger<NotificationAreaController> Logger { protected get; init; }

        void IStartable.Start()
        {
            manager.UserLogon += Manager_SessionChanged;
            manager.RemoteConnect += Manager_SessionChanged;
            manager.ConsoleConnect += Manager_SessionChanged;
            manager.RemoteDisconnect += Manager_SessionChanged;
            manager.ConsoleDisconnect += Manager_SessionChanged;
            manager.UserLogoff += Manager_SessionChanged;

            system.SleeplessChanged += System_SleeplessChanged;

            UpdateTrayIcon();

            Logger.LogDebug("started");
        }

        private void System_SleeplessChanged(object? sender, EventArgs e)
        {
            UpdateTrayIcon();
        }
        private void Manager_SessionChanged(object? sender, ISession session)
        {
            UpdateTrayIcon();
        }

        private void UpdateTrayIcon()
        {
            foreach (var session in manager)
                UpdateTrayIcon(session);
        }

        private void UpdateTrayIcon(ISession session)
        {
            var message = new NotificationAreaMessage(new()
            {
                SleeplessIfUsage = system.SleeplessIfUsage,
                SleeplessUntil = system.SleeplessUntil,

                Sessions = manager.Select(s =>
                {
                    var canControl = session.CanControlSession(s);

                    return new SessionInfo()
                    {
                        Id = s.Id,
                        Name = s.UserName,
                        ClientName = s.ClientName,

                        IsConsoleConnected = s.IsConsoleConnected,
                        IsRemoteConnected = s.IsRemoteConnected,

                        MayConnectToConsole = canControl,
                        MayDisconnect = canControl
                    };
                }).ToDictionary(s => s.Id),

                MayConfigureSleepless = session.CanControlPower(),
                MaySuspendSystem = session.CanControlPower()
            });

            session.SendMessage(message);
        }

        #region Message-Handlers
        void ISessionMessageHandler<DisconnectMessage>.Handle(ISession session, DisconnectMessage message)
        {
            manager.FindSessionByID(message.SessionID)?.Disconnect();
        }

        void ISessionMessageHandler<ConnectToConsoleMessage>.Handle(ISession session, ConnectToConsoleMessage message)
        {
            uint cSID = manager.ConsoleSession?.Id ?? 0;

            try
            {
                // TODO: Berechtigung prüfen
                manager.ConsoleSession = message.SessionID != null ? manager[message.SessionID.Value] : null;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Could not connect SID={message.SessionID} to Console (sid={cSID}).");
            }
        }

        void ISessionMessageHandler<EnterStandbyMessage>.Handle(ISession session, EnterStandbyMessage message)
        {
            try
            {
                if (session.IsRemoteConnected)
                    session.Disconnect();

                power.Suspend();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Could not suspend the system.");
            }
        }

        void ISessionMessageHandler<SleeplessMessage>.Handle(ISession session, SleeplessMessage message)
        {
            system.SleeplessUntil = message.SleeplessUntil;

            if (message.SleeplessIfUsage != null)
            {
                system.SleeplessIfUsage = message.SleeplessIfUsage;
            }
        }
        #endregion
    }
}
