using Autofac;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Pipe.Config;
using MadWizard.Desomnia.Pipe.Messages;
using MadWizard.Desomnia.Service;
using MadWizard.Desomnia.Service.Bridge.Configuration;
using MadWizard.Desomnia.Service.Bridge.Minion;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace MadWizard.Desomnia.Session.Manager.Bridged
{
    public class BridgedTerminalServicesManager(BridgeConfig config, WindowsService? service = null) : TerminalServicesManager(service), IHostedService
    {
        public required IComponentContext ComponentContext { protected get; init; }

        protected override TerminalServicesSession ConfigureSession(uint sid)
        {
            var session = new BridgedTerminalServicesSession(sid)
            {
                Processes = ProcessManager.WithSessionId(sid),
            };

            ConfigureSessionCapabilities(session);

            if (config.SessionMonitor?.SpawnMinions ?? false)
            {
                try
                {
                    var minion = session.LaunchMinion(new()
                    {
                        PushInterval = config.Timeout
                    });

                    minion.MessageReceived += Minion_MessageReceived;
                    minion.Terminated += Minion_Terminated;

                }
                catch (IOException ex)
                {
                    Logger.LogError(ex, "Failed to start session minion for ID=" + sid);
                }
            }

            return session;
        }


        private void ConfigureSessionCapabilities(BridgedTerminalServicesSession session)
        {
            config.SessionMonitor?.Configure(session, desc =>
            {
                if (desc.AllowControlSession != null)
                    if (desc.AllowControlSession?.IsMatchingNone ?? false)
                        session.SessionControl = SessionMatcher.None;
                    else
                        session.SessionControl += desc.AllowControlSession;
                if (desc.AllowControlSleep != null)
                    session.PowerControl = desc.AllowControlSleep.Value;
            });
        }

        private void Minion_MessageReceived(object? sender, UserMessage message) 
        {
            var minion = (SessionMinion)sender!;
            var session = this[minion.SessionID];

            typeof(BridgedTerminalServicesManager)
                .GetMethod(nameof(Minion_HandleMessage), BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(message.GetType())
                .Invoke(this, [session, message]);
        }

        private void Minion_Terminated(object? sender, EventArgs e)
        {
            var minion = (SessionMinion)sender!;

            Logger.LogDebug("Minion terminated: {SessionID}", minion.SessionID);
        }

        private void Minion_HandleMessage<T>(ISession session, T message) where T : UserMessage
        {
            var handlers = ComponentContext.Resolve<IEnumerable<ISessionMessageHandler<T>>>();

            foreach (var handler in handlers)
                using (Logger.BeginScope("Invoking Handler: {HandlerType}", handler.GetType()))
                    try
                    {
                        handler.Handle(session, message);
                    }
                    catch (Exception exception)
                    {
                        Logger.LogError(exception, "SessionMessageHandler-Error");
                    }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /**
         * Initiates async shutdown of all minions, so that the service waits for them to terminate.
         */
        public Task StopAsync(CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();

            foreach (var session in Sessions.Values)
            {
                if (session is BridgedTerminalServicesSession bridged)
                {
                    if (bridged.Minion != null)
                    {
                        tasks.Add(bridged.Minion!.Terminate(TimeSpan.FromSeconds(5)));
                    }
                }
            }

            return Task.WhenAll(tasks);
        }
    }

    internal class BridgedTerminalServicesSession : TerminalServicesSession
    {
        private DateTime? _lastInputTime;

        internal BridgedTerminalServicesSession(uint id) : base(id)
        {

        }

        internal SessionMinion? Minion { get; private set; }

        internal SessionMinion LaunchMinion(MinionConfig config)
        {
            Minion = new SessionMinion(config, this);
            {
                Minion.MessageReceived += Minon_MessageReceived;
                Minion.Terminated += Minion_Terminated;
            }

            return Minion;
        }

        private void Minon_MessageReceived(object? sender, UserMessage message)
        {
            switch (message)
            {
                case InputTimeMessage input:
                    _lastInputTime = input.LastInputTime;
                    break;
            }
        }

        private void Minion_Terminated(object? sender, EventArgs e)
        {
            Minion?.Dispose();
            Minion = null;
        }

        internal SessionMatcher SessionControl { get; set; } = SessionMatcher.None;
        internal bool PowerControl { get; set; } = false;

        public override DateTime? LastInputTime => _lastInputTime ?? base.LastInputTime;

        public override async Task Lock()
        {
            if (Minion != null)
            {
                Minion.Send(new LockMessage());
            }
            else
            {
                throw new NotSupportedException("Minion not available");
            }
        }

        public override void Dispose()
        {
            Minion?.Dispose();
            Minion = null;

            base.Dispose();
        }
    }

    public static class BridgedTerminalServicesExtension
    {
        public static void SendMessage(this ISession session, UserMessage message)
        {
            (((BridgedTerminalServicesSession)session).Minion)?.Send(message);
        }

        internal static bool CanControlSession(this ISession session, ISession anotherSession)
        {
            if (session == anotherSession && ((BridgedTerminalServicesSession)session).SessionControl.IsMatchingSelf)
                return true;

            return ((BridgedTerminalServicesSession)session).SessionControl.Match(session.UserName);
        }

        internal static bool CanControlPower(this ISession session)
        {
            return ((BridgedTerminalServicesSession)session).PowerControl;
        }
    }

    public interface ISessionMessageHandler<T> where T : UserMessage
    {
        void Handle(ISession session, T message);
    }
}
