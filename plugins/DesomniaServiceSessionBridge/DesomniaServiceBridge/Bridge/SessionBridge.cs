using Autofac;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Pipe.Messages;
using MadWizard.Desomnia.Service.Bridge.Configuration;
using MadWizard.Desomnia.Service.Bridge.Minion;
using MadWizard.Desomnia.Session.Manager;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace MadWizard.Desomnia.Service.Bridge
{
    public class SessionBridge(BridgeConfig config, ISessionManager manager) : IHostedService
    {
        public required ILogger<SessionBridge> Logger { protected get; init; }

        public required IComponentContext ComponentContext { protected get; init; }

        internal void ConfigureSession(Session session)
        {
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
                    Logger.LogError(ex, "Failed to start session minion for ID=" + session.Id);
                }
            }
        }

        private void ConfigureSessionCapabilities(Session session)
        {
            config.SessionMonitor?.Configure(session, (config, desc) =>
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
            var session = manager[minion.SessionID];

            typeof(SessionBridge)
                .GetMethod(nameof(Minion_HandleMessage), BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(message.GetType())
                .Invoke(this, [session, message]);
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

        private void Minion_Terminated(object? sender, EventArgs e)
        {
            var minion = (SessionMinion)sender!;

            Logger.LogDebug("Minion terminated: {SessionID}", minion.SessionID);
        }

        Task IHostedService.StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /**
         * Initiates async shutdown of all minions, so that the service waits for them to terminate.
         */
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();

            foreach (var session in manager.OfType<Session>())
            {
                tasks.Add(session.Minion?.Shutdown(TimeSpan.FromSeconds(5)) ?? Task.CompletedTask);
            }

            await Task.WhenAll(tasks);
        }
    }

    public interface ISessionMessageHandler<T> where T : UserMessage
    {
        void Handle(ISession session, T message);
    }
}
