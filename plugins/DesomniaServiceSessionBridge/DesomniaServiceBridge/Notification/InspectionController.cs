using Autofac;
using Autofac.Core.Registration;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Pipe.Messages;
using MadWizard.Desomnia.Session;
using MadWizard.Desomnia.Session.Manager;
using MadWizard.Desomnia.Session.Manager.Bridged;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Service.Bridge.Notification
{
    class InspectionController(ISessionManager manager, SystemUsageInspector inspector) : IStartable, 
        ISessionMessageHandler<RequestInspectionMessage>
    {
        public required ILogger<InspectionController> Logger { protected get; init; }

        public required IComponentContext ComponentContext { protected get; init; }

        void IStartable.Start()
        {
            inspector.Inspected += (sender, args) => SendInspectionData();
            manager.UserLogon += (sender, session) => SendInspectionData(session);

            SendInspectionData();

            Logger.LogDebug("started");
        }

        private void SendInspectionData()
        {
            foreach (var session in manager)
                SendInspectionData(session);
        }

        private void SendInspectionData(ISession session)
        {
            var message = new InspectionMessage
            {
                Time = inspector.LastTime,
                NextTime = inspector.NextTime,

                Tokens = ConvertTokens(inspector.LastTokens),
            };

            session.SendMessage(message);
        }

        private IList<UsageTokenInfo> ConvertTokens(IEnumerable<UsageToken> tokens)
        {
            return tokens.Where(FilterToken).Select(ConvertToken).ToList();
        }

        private bool FilterToken(UsageToken token)
        {
            return token switch
            {
                SleeplessToken => false,

                _ => true,
            };
        }

        private UsageTokenInfo ConvertToken(UsageToken token)
        {
            var info = token switch
            {
                SessionUsageToken session => new UsageTokenInfo
                {
                    DisplayName = session.IsRemote ? @$"{session.ClientName}\{session.UserName}" : session.UserName,
                    TypeName = session.IsRemote ? "Remote Desktop Session" : "User Session",
                },

                NetworkHostUsage host => new UsageTokenInfo
                {
                    DisplayName = host.Name,
                    TypeName = host.Host is VirtualNetworkHost ? "Virtueller Computer" : "Computer",
                },

                NetworkServiceUsage service => new UsageTokenInfo
                {
                    DisplayName = service.Service.Name,
                    TypeName = "Netzwerkdienst",
                },

                _ => ConvertUnknownToken(token)
            };

            info.Tokens = ConvertTokens(token.Tokens);

            return info;
        }

        private UsageTokenInfo ConvertUnknownToken<T>(T token) where T : UsageToken
        {
            try
            {
                var descriptor = ComponentContext.Resolve(typeof(ITokenDescriptor<>).MakeGenericType(token.GetType())) as ITokenDescriptor<T>;

                return descriptor.DescribeToken(token);
            }
            catch (ComponentNotRegisteredException)
            {
                return new UsageTokenInfo
                {
                    DisplayName = token.ToString(),
                    TypeName = token.GetType().Name,
                };
            }
        }

        #region Message-Handlers
        void ISessionMessageHandler<RequestInspectionMessage>.Handle(ISession session, RequestInspectionMessage message)
        {
            inspector.InspectNow();
        }
        #endregion
    }
}
