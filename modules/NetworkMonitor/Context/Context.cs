using Autofac;
using MadWizard.Desomnia.Network.Filter;
using Microsoft.Extensions.Logging;
using System.Net;

namespace MadWizard.Desomnia.Network.Context
{
    public abstract class Context : IDisposable
    {
        protected ILogger Logger { get; set; } = null!;

        protected internal ILifetimeScope Scope
        {
            get;

            init
            {
                field = value;

                Logger = (ILogger) value.Resolve(typeof(ILogger<>).MakeGenericType(GetType()));
            }
        } = null!;

        protected void RegisterTrafficFilter(ContainerBuilder builder, IPPort port)
        {
            RegisterTrafficFilter(builder, port.Protocol switch
            {
                IPProtocol.TCP => new TCPTrafficType(port),
                IPProtocol.UDP => new UDPTrafficType(port),

                _ => throw new NotImplementedException("Unknown IP protocol: " + port.Protocol),
            });
        }

        protected void RegisterTrafficFilter(ContainerBuilder builder, params ITrafficType[] traffic)
        {
            builder.RegisterType<TrafficFilterRequest>()
                .WithParameter(TypedParameter.From(traffic))
                .AutoActivate();
        }

        public virtual void Dispose()
        {
            Scope?.Dispose();
        }
    }
}
