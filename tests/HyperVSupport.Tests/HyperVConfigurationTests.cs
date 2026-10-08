using Autofac;
using Autofac.Builder;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Xml;
using MadWizard.Desomnia.Network.HyperV.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MadWizard.Desomnia.Network.HyperV.Tests
{
    public class HyperVConfigurationTests
    {
        [Theory]
        [InlineData(null, VirtualTraffic.Internal | VirtualTraffic.External)]
        [InlineData("internal|external", VirtualTraffic.Internal | VirtualTraffic.External)]
        [InlineData("external|internal", VirtualTraffic.Internal | VirtualTraffic.External)]
        [InlineData("internal", VirtualTraffic.Internal)]
        [InlineData("external", VirtualTraffic.External)]
        [InlineData("none", VirtualTraffic.None)]
        public void SystemDirectiveConfiguresDeviceMiddlewareAtStartup(string? value, VirtualTraffic expected)
        {
            var path = Path.Combine(Path.GetTempPath(), $"Desomnia-HyperV-{Guid.NewGuid():N}.xml");
            File.WriteAllText(path, (value is null ? "" : $"<?system HyperV:watchVirtualTraffic=\"{value}\" ?>\n")
                + "<SystemMonitor />");
            var source = new ExtendedXmlConfigurationSource(path);
            try
            {
                using var configuration = new ConfigurationManager();
                ((IConfigurationBuilder)configuration).Add(((IRootConfigurationSource)source).RootSource);
                var module = new TestPluginModule();
                module.LoadStartup(configuration);
                Assert.Equal(expected, GetMiddleware(module).WatchVirtualTraffic);

                // Editing configuration does not change the startup choice in a rebuilt app.
                configuration["HyperV:watchVirtualTraffic"] = expected == VirtualTraffic.None ? "external" : "none";
                Assert.Equal(expected, GetMiddleware(module).WatchVirtualTraffic);
            }
            finally
            {
                (source.FileProvider as IDisposable)?.Dispose();
                File.Delete(path);
            }
        }

        [Theory]
        [InlineData("true")]
        [InlineData("false")]
        [InlineData("unknown")]
        [InlineData("internal|unknown")]
        public void InvalidModeFailsStartup(string value)
        {
            using var configuration = new ConfigurationManager();
            configuration["HyperV:watchVirtualTraffic"] = value;
            var error = Assert.Throws<ConfigurationValueException>(() => new TestPluginModule().LoadStartup(configuration));
            Assert.Contains("HyperV:WatchVirtualTraffic", error.Message);
        }

        private static HyperVDeviceDetector GetMiddleware(PluginModule module)
        {
            HyperVDeviceDetector? middleware = null;
            var builder = new ContainerBuilder();
            builder.RegisterModule(module);
            // Observe the pipeline after the plugin has installed its middleware.
            builder.RegisterCallback(registry => registry.Registered += (_, args) =>
            {
                if (args.ComponentRegistration.IsLimitedTo<NetworkDevice>())
                    args.ComponentRegistration.PipelineBuilding += (_, pipeline) =>
                        middleware = Assert.Single(pipeline.Middleware.OfType<HyperVDeviceDetector>());
            });
            builder.RegisterType<NetworkDevice>();
            using var container = builder.Build(ContainerBuildOptions.IgnoreStartableComponents);
            return Assert.IsType<HyperVDeviceDetector>(middleware);
        }

        private sealed class TestPluginModule : PluginModule
        {
            internal void LoadStartup(IConfiguration configuration) => LoadOnce(new ContainerBuilder(), configuration);
        }
    }
}
