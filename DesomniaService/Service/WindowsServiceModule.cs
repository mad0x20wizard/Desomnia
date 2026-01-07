using Autofac;

namespace MadWizard.Desomnia.Service.Windows
{
    internal class WindowsServiceModule : Desomnia.Module
    {
        protected override void Load(Autofac.ContainerBuilder builder)
        {
            // Attaching Windows Control
            builder.RegisterType<WindowsService>()
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();
        }
    }
}
