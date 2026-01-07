using Autofac;
using System.Reflection;

namespace MadWizard.Desomnia
{
    public static class ModuleRegistrationExtensions
    {
        public static void RegisterModule<TModule>(this ApplicationBuilder builder) where TModule : Desomnia.Module, new()
        {
            var module = Activator.CreateInstance<TModule>();

            builder.RegisterModule(module);
        }

        public static void RegisterModuleAssembly(this ApplicationBuilder builder, Assembly assembly)
        {
            var moduleFinder = new ContainerBuilder();

            moduleFinder.RegisterAssemblyTypes(assembly)
                .Where(t => typeof(Module).IsAssignableFrom(t))
                .PropertiesAutowired()
                .As<Module>();

            using (var moduleContainer = moduleFinder.Build())
            {
                foreach (var module in moduleContainer.Resolve<IEnumerable<Module>>())
                {
                    builder.RegisterModule(module);
                }
            }
        }
    }
}
