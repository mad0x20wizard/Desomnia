using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;

namespace MadWizard.Desomnia
{
    public static class PluginLoadingExtensions
    {
        private static IEnumerable<Assembly> EnumPlugins(string path)
        {
            if (Directory.Exists(path = Path.GetFullPath(path)))
            {
                // extract path
                foreach (var zipFile in Directory.GetFiles(path, "plugin-*.zip"))
                {
                    // example: "plugin-FirewallKnockOperator_v3.0.0-beta8.zip"
                    var name = Path.GetFileNameWithoutExtension(zipFile);
                        name = name.Replace("plugin-", string.Empty);
                        name = name.Split("_")[0];

                    ZipFile.ExtractToDirectory(zipFile, Path.Combine(path, name));
                    File.Delete(zipFile);
                }

                // register path
                foreach (var pluginDir in Directory.GetDirectories(path))
                {
                    var pluginName = new DirectoryInfo(pluginDir).Name;
                    var pluginPath = Path.Combine(pluginDir, $"{pluginName}.dll");

                    if (File.Exists(pluginPath))
                    {
                        var pluginContext = new PluginLoadContext(pluginPath);

                        yield return pluginContext.PluginAssembly;
                    }
                }
            }
        }

        public static void RegisterPluginModules(this ApplicationBuilder builder, string path)
        {
            foreach (var assembly in EnumPlugins(path))
            {
                builder.RegisterModuleAssembly(assembly);
            }
        }
    }

    file class PluginLoadContext(string path) : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver resolver = new(path);

        protected override Assembly? Load(AssemblyName assemblyName) => resolver.ResolveAssemblyToPath(assemblyName) is string assemblyPath ? LoadFromAssemblyPath(assemblyPath) : null;

        protected override nint LoadUnmanagedDll(string unmanagedDllName) => resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is string libraryPath ? LoadUnmanagedDllFromPath(libraryPath) : 0;

        public Assembly PluginAssembly => LoadFromAssemblyName(AssemblyName.GetAssemblyName(path));
    }
}
