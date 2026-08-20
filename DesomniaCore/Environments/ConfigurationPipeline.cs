using Autofac;
using MadWizard.Desomnia.Configuration.Model;
using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Migration;
using MadWizard.Desomnia.Configuration.Xml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Runtime.ExceptionServices;
using MadWizard.Desomnia.Application;

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
    /// it: a change of the root element (mode switch), and any invalid edit (malformed
    /// XML, unknown conditions, bad values). The <c>&lt;?system?&gt;</c> directives are
    /// not the pipeline's business: they are the root host's configuration, served (and
    /// reloaded) by the source's own nested root source.</para>
    /// </summary>
    internal sealed class ConfigurationPipeline : IStartable, IDisposable
    {
        public required ILogger Logger { private get; init; }

        // the file watcher's raw events coalesce over this window, so a re-read only happens
        // after the editor has finished writing
        static readonly TimeSpan FileDebounce = TimeSpan.FromMilliseconds(500);

        readonly ExtendedXmlConfigurationSource _source;
        readonly string _configPath;
        readonly EnvironmentMonitor _monitor;
        readonly ILifetimeScope _scope;

        // the module registry: its version check is the authority that refuses a mismatched
        // file, AFTER the migration (if any) had its chance and independent of it
        readonly ModuleRegistry _registry;

        readonly Lock _lock = new();

        bool _augmenting;

        // the file text of the current generation, as the source's provider SERVES it (see
        // TryReadText) - a watcher event that yields the same served content skips the
        // re-parse (a spurious event, or a migration's own file write)
        string? _readText;

        // passthrough only: the flattened data of the current generation - the rebuild
        // criterion (the augmenting mode's monitor compares its effective data the same way)
        IReadOnlyList<KeyValuePair<string, string?>>? _pairs;

        // a change the process cannot apply: recorded here, surfaced to the application
        // loop through ThrowIfFailed after the reload signal wakes it
        Exception? _failure;

        IDisposable? _watch;
        Timer? _debounce;

        bool _disposed;

        public ConfigurationPipeline(ExtendedXmlConfigurationSource source, EnvironmentMonitor monitor, ILifetimeScope scope,
            ModuleRegistry registry)
        {
            _source = source;
            _configPath = source.FullPath!;
            _monitor = monitor;
            _scope = scope;
            _registry = registry;

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

                    // the version check, AFTER the migration (if composed) had its chance: a
                    // file this build cannot use fails the boot right here
                    _registry.Validate(file.Version);

                    _augmenting = DetectAugmenting(file.RootName);

                    if (_augmenting)
                    {
                        var (settings, blocks, conditions) = Materialize(file);

                        _monitor.Initialize(settings, blocks, _source.Collections, conditions);

                        EffectiveSource = _monitor.ConfigurationSource;
                    }
                    else
                    {
                        _pairs = Flatten(file);
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

                string? text;
                try
                {
                    // the SERVED text (see TryReadText): a migration's own file write serves
                    // the identical text again and falls out of the comparison below - while a
                    // migration that ran on the providers' earlier read of a user edit serves
                    // that edit's data, which the comparison duly catches. Only the data decides.
                    text = TryReadText();
                }
                catch (Exception ex)
                {
                    // the composition refused to serve the file (a malformed edit, a failing
                    // migration): fatal by design - exit with an error code and let the
                    // service manager restart the application (self-heal)
                    Fail(ex);
                    return;
                }

                if (text is null)
                    return; // an environment hiccup (see TryReadText) - the next watcher event retries

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

            // an edit to a version this build cannot use is a fatal change like any other
            _registry.Validate(file.Version);

            bool augmenting = DetectAugmenting(file.RootName);

            if (augmenting != _augmenting)
                throw new ConfigurationValueException($"The configuration root changed to <{file.RootName}>; " +
                    "switching the configuration mode requires a restart.");

            _readText = text;

            if (!augmenting)
            {
                // no environments to re-merge: if the data the host reads changed, the loop
                // rebuilds and the host's provider re-reads the file. An edit that leaves the
                // data alone - formatting, comments, the <?config?> header, the <?system?>
                // directives (which the root host's own source follows) - is no reason to
                // restart the application.
                var pairs = Flatten(file);

                if (_pairs is not null && _pairs.SequenceEqual(pairs))
                    return;

                _pairs = pairs;

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

        /// <summary>
        /// The file's text as the source's file provider serves it — the same content every
        /// other consumer of the source reads: with the migration layer composed underneath
        /// (a provider decorator, see the application builder), the migrated form; without it,
        /// the file as-is. The pipeline knows no difference. Returns null for a missing file or
        /// an environment hiccup (a locked or vanishing file — the next event retries); any
        /// other failure of the composition (a malformed file, a failing migration) propagates:
        /// a configuration that cannot be served stops the application.
        /// </summary>
        private string? TryReadText()
        {
            if (_source.FileProvider is not { } provider || _source.Path is not string path)
                return null; // no file to read; the host's own provider reports it

            try
            {
                var file = provider.GetFileInfo(path);

                if (!file.Exists)
                    return null;

                using var stream = file.CreateReadStream();

                return ConfigurationText.Decode(stream);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
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

            // the version the (possibly migrated) document actually declares - already validated
            var settings = new EnvironmentSettings(file.Version, result.Debounce, result.OnConflict,
                ResolveOutputPath(result.WriteEffectiveXML),
                ResolveOutputPath(result.WriteEffectiveConfiguration));

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

        /// <summary>The data the host's provider would serve for the file (passthrough mode).</summary>
        private IReadOnlyList<KeyValuePair<string, string?>> Flatten(XmlConfigurationFile file)
            => ConfigNodeFlattener.Flatten(file.ToConfigNode(), _source.Collections);

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
