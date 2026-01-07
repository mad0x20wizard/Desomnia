using Autofac;
using MadWizard.Desomnia.Network.Filter;

namespace MadWizard.Desomnia.Network.Context
{
    public abstract class Context
    {
        protected void RegisterTrafficFilter(ContainerBuilder builder, params ITrafficType[] traffic)
        {
            builder.RegisterType<TrafficFilterRequest>()
                .WithParameter(TypedParameter.From(traffic))
                .AutoActivate();
        }
    }
}
