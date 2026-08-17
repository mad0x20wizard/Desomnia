using Autofac;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Xml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Runtime.ExceptionServices;

namespace MadWizard.Desomnia.Environments
{
    /// <summary>
    /// The stage between the physical configuration source and the
    /// <see cref="EnvironmentMonitor"/>. At boot it reads the file once, decides the mode
    /// from the root element — a classic &lt;SystemMonitor&gt; passes the original source
    /// through to the host untouched, an &lt;EnvironmentMonitor&gt; root parses into
    /// environment blocks, binds their conditions out of the persistent container and hands
    /// them to the monitor, whose own source then faces the host (see
    /// <see cref="EffectiveSource"/>).
    ///
    /// <para>When the source watches for changes (<c>ReloadOnChange</c>), the pipeline
    /// re-reads the file on every settled edit and pumps the re-parsed blocks into the
    /// monitor. Changes the running process cannot apply are FATAL by design — the
    /// application exits with an error code and relies on the service manager to restart
    /// it: a change of the persistent configuration (the <c>&lt;?global?&gt;</c>
    /// directives), a change of the root element (mode switch), and any invalid edit
    /// (malformed XML, unknown conditions, bad values).</para>
    /// </summary>
    internal sealed class ConfigurationPipeline : IStartable, IDisposable
    {
        public required ILogger Logger { private get; init; }

        // the file watcher's raw events coalesce over this window, so a re-read only happens
        // after the editor has finished writing
        static readonly TimeSpan FileDebounce = TimeSpan.FromMilliseconds(500);

        readonly ExtendedXmlConfigurationSource _source;
        readonly string _configPath;
        readonly PersistentConfiguration _persistentConfiguration;
        readonly EnvironmentMonitor _monitor;
        readonly ILifetimeScope _scope;

        readonly Lock _lock = new();

        bool _augmenting;

        // the raw file text of the current generation - a spurious watcher event that
        // reports the same content skips the re-parse
        string? _readText;

        // a change the process cannot apply: recorded here, surfaced to the application
        // loop through ThrowIfFailed after the reload signal wakes it
        Exception? _failure;

        IDisposable? _watch;
        Timer? _debounce;

        bool _disposed;

        public ConfigurationPipeline(ExtendedXmlConfigurationSource source,
            PersistentConfiguration persistentConfiguration, EnvironmentMonitor monitor, ILifetimeScope scope)
        {
            _source = source;
            _configPath = source.FullPath!;
            _persistentConfiguration = persistentConfiguration;
            _monitor = monitor;
            _scope = scope;

            EffectiveSource = source;
        }

        /// <summary>The configuration source the host consumes: the physical source
        /// (passthrough) or the monitor's own source (augmenting) - decided by
        /// <see cref="Start"/>.</summary>
        internal IConfigurationSource EffectiveSource { get; private set; }

        /// <summary>Whether the configuration declares environments. Test seam.</summary>
        internal bool Augmenting
        {
            get { lock (_lock) return _augmenting; }
        }

        /// <summary>
        /// Reads the configuration once, decides the mode, feeds the monitor (augmenting)
        /// and starts watching the file (when the source reloads on change). Called once at
        /// boot; a configuration problem here is fatal - there is nothing to fall back to.
        /// </summary>
        public void Start()
        {
            lock (_lock)
            {
                // a missing (or unreadable) file stays passthrough: the non-optional provider
                // reports it when the first application configuration is built
                if (TryReadText() is string text)
                {
                    _readText = text;

                    var file = XmlConfigurationReader.Read(text);

                    _augmenting = DetectAugmenting(file.RootName);

                    if (_augmenting)
                    {
                        var (settings, blocks, conditions) = Materialize(file);

                        _monitor.Initialize(settings, blocks, _source.Collections, conditions);

                        EffectiveSource = _monitor.ConfigurationSource;
                    }
                }

                if (_source.ReloadOnChange)
                    StartWatching();
            }
        }

        /// <summary>Runs one settled-file check synchronously, as if the debounced watcher
        /// had fired. Test seam (production runs off the real watcher + timer).</summary>
        internal void CheckForChanges() => OnFileSettled(null);

        /// <summary>Surfaces a recorded fatal configuration change to the application loop
        /// (which lets it escape to the process entry point - outside any container).</summary>
        internal void ThrowIfFailed()
        {
            lock (_lock)
            {
                if (_failure is Exception failure)
                    ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        #region File watching

        // the stock file-provider watch (the same machinery the configuration framework
        // uses), debounced by our one-shot timer
        private void StartWatching()
        {
            if (_source.FileProvider is not { } provider || _source.Path is not string path)
            {
                Logger.LogWarning("Auto-reload is enabled, but the configuration source has no file to watch.");
                return;
            }

            _debounce = new Timer(OnFileSettled);

            _watch = ChangeToken.OnChange(() => provider.Watch(path), OnFileChanged);
        }

        // a raw watcher event only (re)arms the debounce; the settle callback does the work
        private void OnFileChanged()
        {
            lock (_lock)
            {
                if (!_disposed)
                    _debounce?.Change(FileDebounce, Timeout.InfiniteTimeSpan);
            }
        }

        private void OnFileSettled(object? state)
        {
            lock (_lock)
            {
                if (_disposed || _failure is not null)
                    return;

                string text;
                try
                {
                    text = File.ReadAllText(_configPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
                {
                    // an environment hiccup (a locked or vanished file), not an edit - the
                    // next watcher event retries
                    Logger.LogWarning(ex, $"Failed to read the configuration file '{_configPath}'; keeping the current configuration.");
                    return;
                }

                if (text == _readText)
                    return; // no material change (a touch, or a spurious watcher event)

                try
                {
                    Apply(text);
                }
                catch (Exception ex)
                {
                    // a bad or incompatible edit is fatal by design: exit with an error code
                    // and let the service manager restart the application (self-heal)
                    Fail(ex);
                }
            }
        }

        // under _lock
        private void Apply(string text)
        {
            var file = XmlConfigurationReader.Read(text);

            if (!_persistentConfiguration.Matches(Entries(file)))
                throw new ConfigurationValueException("The persistent configuration (the <?global?> directives) " +
                    "changed; it configures the persistent container and can only be applied by a restart.");

            bool augmenting = DetectAugmenting(file.RootName);

            if (augmenting != _augmenting)
                throw new ConfigurationValueException($"The configuration root changed to <{file.RootName}>; " +
                    "switching the configuration mode requires a restart.");

            _readText = text;

            if (!augmenting)
            {
                // no environments to re-merge - the loop rebuilds, and the host's provider
                // re-reads the file
                _monitor.SignalReloadRequest("Configuration file changed");
                return;
            }

            var (settings, blocks, conditions) = Materialize(file);

            _monitor.Update(settings, blocks, conditions);
        }

        // under _lock
        private void Fail(Exception failure)
        {
            _failure = failure;

            Logger.LogCritical(failure, "The configuration change cannot be applied in-process. " +
                "Exiting with an error code, so the service manager restarts the application.");

            _monitor.SignalReloadRequest("Fatal configuration change");
        }

        #endregion

        #region Reading

        private string? TryReadText()
        {
            try
            {
                return File.Exists(_configPath) ? File.ReadAllText(_configPath) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.LogWarning(ex, $"Failed to read the configuration file '{_configPath}'.");
                return null;
            }
        }

        /// <summary>An &lt;EnvironmentMonitor&gt; root augments; the classic
        /// &lt;SystemMonitor&gt; passes through. Anything else is an error.</summary>
        private static bool DetectAugmenting(string rootName)
        {
            if (rootName.Equals(EnvironmentParser.SYSTEM_MONITOR_ELEMENT, StringComparison.OrdinalIgnoreCase))
                return false;

            if (rootName.Equals(EnvironmentParser.ROOT_ELEMENT, StringComparison.OrdinalIgnoreCase))
                return true;

            throw new ConfigurationValueException($"Unknown configuration root element <{rootName}>; " +
                $"expected <{EnvironmentParser.SYSTEM_MONITOR_ELEMENT}> or <{EnvironmentParser.ROOT_ELEMENT}>.");
        }

        /// <summary>Parses the environments and binds their conditions out of the persistent
        /// container (in a fresh child scope, which the monitor adopts and later retires).</summary>
        private (EnvironmentSettings Settings, IReadOnlyList<EnvironmentBlock> Blocks, ILifetimeScope Conditions) Materialize(XmlConfigurationFile file)
        {
            var result = EnvironmentParser.Parse(file.Root.Document!);

            var settings = new EnvironmentSettings(result.Version, result.Debounce, result.OnConflict,
                ResolveOutputPath(result.OutputEffectiveXML),
                ResolveOutputPath(result.OutputEffectiveConfiguration));

            var conditions = _scope.BeginLifetimeScope();
            try
            {
                EnvironmentConditionBinder.ResolveConditions(conditions, result.Blocks);
            }
            catch
            {
                conditions.Dispose();
                throw;
            }

            return (settings, result.Blocks, conditions);
        }

        /// <summary>Resolves the output path relative to the configuration file, unless it is absolute.</summary>
        private string? ResolveOutputPath(string? output)
        {
            if (output is null)
                return null;

            string configFullPath = Path.GetFullPath(_configPath);

            string outputPath = Path.GetFullPath(output, Path.GetDirectoryName(configFullPath)!);

            // overwriting the configuration would also feed the file watcher a restart loop
            if (string.Equals(outputPath, configFullPath, StringComparison.OrdinalIgnoreCase))
                throw new ConfigurationValueException("An effective-configuration output " +
                    $"must not point at the configuration file itself ({configFullPath}).");

            return outputPath;
        }

        private static IEnumerable<KeyValuePair<string, string>> Entries(XmlConfigurationFile file)
            => file.GlobalDirectives.Select(directive => new KeyValuePair<string, string>(directive.Key, directive.Value));

        #endregion

        public void Dispose()
        {
            IDisposable? watch;
            Timer? debounce;

            lock (_lock)
            {
                if (_disposed)
                    return;

                _disposed = true;

                (watch, _watch) = (_watch, null);
                (debounce, _debounce) = (_debounce, null);
            }

            // outside the lock: disposing the watch registration BLOCKS until an in-flight
            // change callback returns, and that callback takes _lock (OnFileChanged) - under
            // the lock this would deadlock the shutdown. The callbacks see _disposed and skip.
            watch?.Dispose();

            debounce?.Dispose();
        }
    }
}
