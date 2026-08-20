using MadWizard.Desomnia.Application;

namespace MadWizard.Desomnia
{
    public static class ModuleRegistrationExtensions
    {
        public static void RegisterModule<TModule>(this ApplicationBuilder builder) where TModule : Desomnia.Module, new()
        {
            var module = Activator.CreateInstance<TModule>();

            builder.RegisterModule(module);
        }
    }
}
