using Autofac;
using System.Reflection;

namespace MadWizard.Desomnia.Application.Registry
{
    /// <summary>
    /// The central authority over the module system: every <see cref="Module"/> of the product
    /// is registered here exactly once (the <see cref="ApplicationBuilder"/> creates and keeps
    /// the registry for the life of the process), and everything the product derives FROM the
    /// registered module set is answered here — today the configuration format algebra over the
    /// modules' version facts (<see cref="ConfigurableModule.MinVersion"/>,
    /// <see cref="ConfigurableModule.MaxVersion"/>) and the version check built on it.
    ///
    /// <para><see cref="SupportedVersion"/> is the newest format version every module accepts,
    /// <see cref="RequiredVersion"/> the oldest version every module still reads without
    /// migration — so a file older than the product's latest format stays perfectly valid as
    /// long as no loaded module demands a newer one. <see cref="Validate"/> is the authority
    /// that REFUSES a mismatched file: a document newer than <see cref="SupportedVersion"/>
    /// (a newer configuration on an older build), or one still older than
    /// <see cref="RequiredVersion"/> after the migration had its chance. It stands apart from
    /// the migration layer on purpose: the check holds with the migration detached
    /// (<c>autoMigrate="never"</c>, a composition without the migrating file provider) —
    /// migration is one way to satisfy it, not its owner.</para>
    ///
    /// <para>The algebra is fixed at the first use: registrations (and a change of the latest
    /// version) are refused from then on. Everything is logged before an exception stops the
    /// application, so the log has the specifics.</para>
    /// </summary>
    internal class ModuleRegistry : IIEnumerable<Module>
    {
        protected readonly List<Module> _modules = [];

        protected bool _locked = false;

        #region Registrations
        internal virtual void Register(Module module)
        {
            ArgumentNullException.ThrowIfNull(module);

            lock (this)
            {
                if (_locked)
                    throw new InvalidOperationException("Module list has already been locked.");

                _modules.Add(module);
            }
        }

        internal void RegisterPluginAssembly(Assembly assembly) => RegisterPluginAssembly<Module>(assembly);

        internal void RegisterPluginAssembly<T>(Assembly assembly) where T : Module
        {
            var moduleFinder = new ContainerBuilder();

            moduleFinder.RegisterAssemblyTypes(assembly)
                .Where(t => typeof(T).IsAssignableFrom(t))
                .PropertiesAutowired()
                .As<T>();

            using (var moduleContainer = moduleFinder.Build())
            {
                foreach (var module in moduleContainer.Resolve<IEnumerable<T>>())
                {
                    Register(module);
                }
            }
        }
        #endregion

        internal virtual void Lock()
        {
            _locked = true;
        }

        IEnumerator<Module> IEnumerable<Module>.GetEnumerator() => _modules.GetEnumerator();
    }
}
