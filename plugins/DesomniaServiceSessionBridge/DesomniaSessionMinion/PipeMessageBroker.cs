using Autofac;
using MadWizard.Desomnia.Pipe;
using MadWizard.Desomnia.Pipe.Messages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.Minion
{
    internal delegate void UserMessageHandler<T>(T message) where T : UserMessage;

    public class PipeMessageBroker
    {
        IHostApplicationLifetime _lifetime;

        private MessagePipeClient _pipe;

        private Dictionary<Type, object> _handlers = new Dictionary<Type, object>();

        public ILogger<PipeMessageBroker> Logger { get; set; }

        public PipeMessageBroker(IHostApplicationLifetime lifetime, ILogger<PipeMessageBroker> logger, MessagePipeClient pipe)
        {
            _lifetime = lifetime;

            Logger = logger;

            _pipe = pipe;

            _pipe.MessageReceived += Pipe_MessageReceived;
            _pipe.Disconnected += Pipe_Disconnected;

            Logger.LogDebug("init");
        }


        private void Pipe_MessageReceived(object sender, Message message)
        {
            Logger.LogDebug($"Received Message: {message.GetType().Name}");

            if (message is SystemMessage)
            {
                if (message is TerminateMessage)
                {
                    Logger.LogInformation("Terminated. Shutting down...");

                    _lifetime.StopApplication();
                }
            }
            else if (message is UserMessage user)
            {
                if (_handlers.TryGetValue(user.GetType(), out object handler))
                {
                    (handler as Delegate).DynamicInvoke(user);
                }
            }
        }

        private void Pipe_Disconnected(object sender, EventArgs e)
        {
            Logger.LogError("Disconnected. Shutting down...");

            _lifetime.StopApplication();
        }

        internal void RegisterMessageHandler<T>(UserMessageHandler<T> handler) where T : UserMessage
        {
            _handlers[typeof(T)] = handler;

            Logger.LogDebug("Registered MessageHandler for: " + typeof(T).Name);
        }

        public void SendMessage(UserMessage message)
        {
            Logger.LogDebug($"Send Message: {message.GetType().Name}");

            _pipe.SendMessage(message);
        }
    }
}
