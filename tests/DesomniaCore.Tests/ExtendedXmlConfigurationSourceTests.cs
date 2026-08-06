using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Xml;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    /// <summary>
    /// The standalone XML file source under real reload conditions.
    /// </summary>
    public class ExtendedXmlConfigurationSourceTests : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("DesomniaTests").FullName;
        private readonly string _path;

        public ExtendedXmlConfigurationSourceTests() => _path = Path.Combine(_directory, "monitor.xml");

        public void Dispose() => Directory.Delete(_directory, recursive: true);

        [Fact]
        public async Task MissingFileDuringReload_KeepsServingTheLastGoodData()
        {
            File.WriteAllText(_path, """<SystemMonitor version="1" timeout="00:00:30" />""");

            var source = new ExtendedXmlConfigurationSource(_path, optional: false, reloadOnChange: true);

            var root = new ConfigurationBuilder().Add(source).Build();

            try
            {
                Assert.Equal("00:00:30", root["timeout"]);

                TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = ChangeToken.OnChange(root.GetReloadToken, () => reloaded.TrySetResult());

                // an editor's delete+rename save leaves a window where the file is missing;
                // the stock reload would clear the data to empty in that window
                File.Delete(_path);

                await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(10));

                // values and child keys still come from the last successful load, consistently
                Assert.Equal("00:00:30", root["timeout"]);
                Assert.Contains(root.GetChildren(), section => section.Key.Equals("timeout", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                (root as IDisposable)?.Dispose(); // stop the watcher before the directory goes away
            }
        }

        [Fact]
        public async Task BadEditDuringReload_KeepsServingTheLastGoodData()
        {
            File.WriteAllText(_path, """<SystemMonitor version="1" timeout="00:00:30" />""");

            var source = new ExtendedXmlConfigurationSource(_path, optional: false, reloadOnChange: true);

            var root = new ConfigurationBuilder().Add(source).Build();

            try
            {
                TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = ChangeToken.OnChange(root.GetReloadToken, () => reloaded.TrySetResult());

                File.WriteAllText(_path, "<SystemMonitor version="); // half-written edit

                await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(10));

                // the pipeline is the authority that exits on a bad edit; until then the
                // provider must not feed the options system an empty configuration
                Assert.Equal("00:00:30", root["timeout"]);
            }
            finally
            {
                (root as IDisposable)?.Dispose();
            }
        }
    }
}
