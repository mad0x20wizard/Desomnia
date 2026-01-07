using Autofac;

namespace MadWizard.Desomnia
{
    public static class RegistrationExtensions
    {
        public static void RegisterSystemMutex(this ContainerBuilder builder, string name, bool global = true)
        {
            builder.RegisterType<SystemMutex>().AutoActivate()
                .WithParameter("name", (global ? @"Global\" : "") + name)
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();
        }
    }
}
