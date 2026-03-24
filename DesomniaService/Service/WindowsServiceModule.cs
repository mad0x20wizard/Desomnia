using Autofac;

namespace MadWizard.Desomnia.Service.Windows
{
    internal class WindowsServiceModule(CancellationToken restartToken) : Desomnia.Module
    {
        protected override void Load(Autofac.ContainerBuilder builder)
        {
            // Attaching Windows Control
            builder.RegisterType<WindowsService>()
                .WithParameter(TypedParameter.From(restartToken))
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();
        }
    }
}
