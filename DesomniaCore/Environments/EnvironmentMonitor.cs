using Autofac;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Configuration.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Environments
{
    /// <summary>
    /// Decides the effective configuration. It receives the environment blocks parsed,
    /// normalized and condition-bound by the earlier pipeline stages (initially and — when
    /// the underlying source supports change detection — again on every configuration file
    /// change, see <see cref="Update"/>), watches the live condition states, merges the
    /// active blocks into one effective configuration tree, and serves the result to the
    /// host through its own <see cref="ConfigurationSource"/> — a drop-in replacement for
    /// any physical configuration source, with reload support.
    ///
    /// <para>It knows nothing about XML, files or containers: its inputs are abstract
    /// blocks with constructed conditions, its outputs are a standard
    /// <see cref="IConfigurationSource"/>, the <see cref="EffectiveChanged"/> event (which
    /// the loosely coupled exporters react to) and the <see cref="ReloadToken"/> — the ONE
    /// change signal the application loop rebuilds on, streamlining all change sources
    /// (file edits and environment conditions alike).</para>
    /// </summary>
    public sealed class EnvironmentMonitor : IDisposable
    {
        public required ILogger Logger { private get; init; }

        readonly Lock _lock = new();

        // the current configuration generation (augmenting mode; empty in passthrough).
        // Replaced wholesale by Update() when the pipeline pumps a re-parsed file in.
        EnvironmentSettings? _settings;
        IReadOnlyList<EnvironmentBlock> _blocks = [];
        CollectionElementRegistry _collections = new();

        // the generation's condition instances live in this scope; the watcher observes them.
        // Both are retired together when a new generation is adopted.
        ILifetimeScope? _conditionScope;
        EnvironmentWatcher? _conditionWatcher;

        // the effective output: the flattened data the provider serves, and the pairs the
        // change comparison runs over
        OrderedConfigurationData _data = OrderedConfigurationData.Empty;
        IReadOnlyList<KeyValuePair<string, string?>> _pairs = [];

        // the reload signal: the application loop links its run against ReloadToken, and a
        // change — a condition, a file edit — cancels it. ArmReload() arms a fresh token per
        // inner build, so a change that lands during a build's startup is never lost.
        CancellationTokenSource _reload = new();

        bool _disposed;

        public EnvironmentMonitor()
        {
            ConfigurationSource = new EnvironmentConfigurationSource(this);
        }

        /// <summary>The monitor's own configuration source — handed to the host instead of
        /// the physical source when the configuration declares environments.</summary>
        internal IConfigurationSource ConfigurationSource { get; }

        /// <summary>The current effective configuration data (document-ordered), pulled by
        /// the source's providers.</summary>
        internal OrderedConfigurationData Data
        {
            get { lock (_lock) return _data; }
        }

        /// <summary>Raised whenever a new effective configuration has been computed — the
        /// providers reload from it, the exporters write their files from it. Raised outside
        /// hot paths but under the monitor's state transitions; handlers must not throw.</summary>
        internal event Action<EffectiveConfiguration>? EffectiveChanged;

        /// <summary>Whether environments drive the configuration (augmenting mode). Test seam.</summary>
        internal bool Augmenting
        {
            get { lock (_lock) return _settings is not null; }
        }

        #region Pipeline input

        /// <summary>
        /// Adopts the first configuration generation (augmenting mode), computes the initial
        /// effective configuration and starts watching the conditions. Called once at boot,
        /// before the first inner host is built.
        /// </summary>
        internal void Initialize(EnvironmentSettings settings, IReadOnlyList<EnvironmentBlock> blocks,
            CollectionElementRegistry collections, ILifetimeScope conditionScope)
        {
            lock (_lock)
            {
                _collections = collections;

                Adopt(settings, blocks, conditionScope, CreateConditionWatcher(blocks, settings.Debounce));

                var (effective, active) = Compute();

                _data = effective.Data;
                _pairs = [.. effective.Data.Pairs];

                LogActive(effective.ActiveDescription, active);

                Publish(effective);
            }
        }

        /// <summary>
        /// Adopts a re-parsed configuration generation, pumped in by the pipeline after a
        /// file change. The new generation's conditions are already constructed; the watcher
        /// subscription (fallible platform work) happens here, before the old generation is
        /// retired — a fault leaves the current generation running and surfaces to the
        /// pipeline (where it is fatal, by design). If the resulting effective configuration
        /// differs, the change is published and the reload token cancelled.
        /// </summary>
        internal void Update(EnvironmentSettings settings, IReadOnlyList<EnvironmentBlock> blocks, ILifetimeScope conditionScope)
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                EnvironmentWatcher? watcher;
                try
                {
                    watcher = CreateConditionWatcher(blocks, settings.Debounce);
                }
                catch
                {
                    conditionScope.Dispose();
                    throw;
                }

                var oldWatcher = _conditionWatcher;
                var oldScope = _conditionScope;
                var oldSettings = _settings;

                Adopt(settings, blocks, conditionScope, watcher);

                // best effort: the new generation is already live, so a fault here is logged, not fatal
                try { oldWatcher?.Dispose(); } catch (Exception ex) { Logger.LogWarning(ex, "Failed to retire the previous environment watcher."); }
                try { oldScope?.Dispose(); } catch (Exception ex) { Logger.LogWarning(ex, "Failed to retire the previous environment conditions."); }

                var (effective, active) = Compute();

                bool dataChanged = !_pairs.SequenceEqual(effective.Data.Pairs);

                if (dataChanged)
                {
                    _data = effective.Data;
                    _pairs = [.. effective.Data.Pairs];

                    LogActive(effective.ActiveDescription, active);
                }

                // the exporters must also see a settings-only edit (a moved, added or removed
                // output path), even when the effective content is unchanged - only a content
                // change needs the rebuild, though
                if (dataChanged || !settings.Equals(oldSettings))
                    Publish(effective);

                if (dataChanged)
                    SignalReload("Configuration file changed");
            }
        }

        /// <summary>The passthrough-mode change signal: the pipeline detected a material file
        /// change (there are no environments to re-merge), so the loop must rebuild.</summary>
        internal void SignalReloadRequest(string reason)
        {
            lock (_lock)
            {
                if (!_disposed)
                    SignalReload(reason);
            }
        }

        #endregion

        // under _lock
        private void Adopt(EnvironmentSettings settings, IReadOnlyList<EnvironmentBlock> blocks,
            ILifetimeScope conditionScope, EnvironmentWatcher? watcher)
        {
            _settings = settings;
            _blocks = blocks;
            _conditionScope = conditionScope;
            _conditionWatcher = watcher;
        }

        /// <summary>
        /// Re-evaluates the live conditions (called off the condition watcher, debounced).
        /// If the resulting effective configuration differs from the one currently served,
        /// publishes it and cancels the reload token so the application loop rebuilds.
        /// </summary>
        internal void Reevaluate()
        {
            lock (_lock)
            {
                if (_disposed || _settings is null)
                    return;

                Refresh(reason: null);
            }
        }

        /// <summary>Recomputes the effective configuration and, if it changed, swaps it in,
        /// publishes it and signals the reload (under _lock).</summary>
        private void Refresh(string? reason)
        {
            var (effective, active) = Compute();

            if (_pairs.SequenceEqual(effective.Data.Pairs))
                return;

            _data = effective.Data;
            _pairs = [.. effective.Data.Pairs];

            LogActive(effective.ActiveDescription, active);

            Publish(effective);

            SignalReload(reason ?? $"Environment -> {effective.ActiveDescription}");
        }

        // under _lock; handlers (providers, exporters) must not throw - guard anyway, since
        // this runs on watcher threads outside any host try/catch
        private void Publish(EffectiveConfiguration effective)
        {
            try
            {
                EffectiveChanged?.Invoke(effective);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "A subscriber failed to process the effective configuration.");
            }
        }

        private (EffectiveConfiguration, IReadOnlyList<EnvironmentBlock>) Compute()
        {
            var settings = _settings!;

            var active = ComputeActiveBlocks(_blocks);

            var root = ConfigMerger.Merge(active, _collections, settings.OnConflict);

            StampVersion(root, settings.Version);

            var data = new OrderedConfigurationData(ConfigNodeFlattener.Flatten(root, _collections));

            return (new EffectiveConfiguration(settings, root, data, DescribeActive(active)), active);
        }

        /// <summary>Regardless of the block shapes, the version attribute always lives on the
        /// configuration root, stamped from the &lt;EnvironmentMonitor&gt; root.</summary>
        private static void StampVersion(ConfigNode root, string version)
        {
            if (root.Children.FirstOrDefault(child => child.Kind == ConfigNodeKind.Attribute
                && child.HasName(EnvironmentParser.VERSION_ATTRIBUTE)) is ConfigNode existing)
            {
                existing.Value = version;
            }
            else
            {
                root.Children.Insert(0, new ConfigNode(EnvironmentParser.VERSION_ATTRIBUTE, ConfigNodeKind.Attribute)
                {
                    Value = version,
                });
            }
        }

        private void LogActive(string description, IReadOnlyList<EnvironmentBlock> active)
        {
            if (active.Count == 0 || active.All(block => block.IsDefault))
                Logger.LogWarning($"Environment -> {description} - no environment condition matches.");
            else
                Logger.LogInformation($"Environment -> {description}");
        }

        #region Reload token

        /// <summary>The reload signal: the application loop links its run against this token,
        /// and a change — a condition or a file edit — cancels it, which ends the run and
        /// rebuilds.</summary>
        internal CancellationToken ReloadToken
        {
            // a cancelled token once disposed (the loop has already stopped by then): it never
            // reads a disposed CTS, and its stopping-token check short-circuits the result
            get { lock (_lock) return _disposed ? new CancellationToken(canceled: true) : _reload.Token; }
        }

        /// <summary>Arms a fresh reload token for one inner build; a change that lands even
        /// during the build's startup cancels it (and never the previous run's, which has
        /// already ended).</summary>
        internal void ResetReloadToken()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                var previous = _reload;
                _reload = new CancellationTokenSource();
                previous.Dispose();
            }
        }

        // under _lock, after the effective configuration has already been updated
        private void SignalReload(string reason)
        {
            Logger.LogInformation($"{reason}. Reloading...");

            try
            {
                _reload.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // already shutting down; the loop reads the token's state, not the reason
            }
        }

        #endregion

        #region Active blocks

        /// <summary>
        /// Evaluates all blocks (each block exactly once), in document order.
        /// Blocks with onlyIf="never" are excluded; default blocks with onlyIf="else"
        /// are included only when no other environment matches. A block referencing
        /// another environment is applied only while that target is applied (onlyIf) or
        /// is not applied (onlyIfNot) - the parser guarantees the combined reference graph
        /// is acyclic, so the memoized recursion below terminates.
        /// </summary>
        private List<EnvironmentBlock> ComputeActiveBlocks(IReadOnlyList<EnvironmentBlock> blocks)
        {
            Dictionary<EnvironmentBlock, bool> applied = [];

            bool IsApplied(EnvironmentBlock block)
            {
                if (applied.TryGetValue(block, out bool result))
                    return result;

                result = block.MergeMode != EnvironmentMergeMode.Never && block.IsActive;

                if (result && IsSuppressed(block))
                {
                    Logger.LogTrace($"Environment '{block.DisplayName}' is suppressed by " +
                        $"{EnvironmentParser.ONLY_IF_NOT_ATTRIBUTE} = \"{block.OnlyIfNot}\".");

                    result = false;
                }

                if (result && !IsRequirementMet(block))
                {
                    Logger.LogTrace($"Environment '{block.DisplayName}' is inactive because its required " +
                        $"{EnvironmentParser.ONLY_IF_ATTRIBUTE} = \"{block.OnlyIf}\" environment is not applied.");

                    result = false;
                }

                return applied[block] = result;
            }

            // suppressed while any environment named by onlyIfNot is applied
            bool IsSuppressed(EnvironmentBlock block)
                => block.OnlyIfNot is string target && IsAnyApplied(target);

            // requirement met unless onlyIf names an environment that is not applied
            bool IsRequirementMet(EnvironmentBlock block)
                => block.OnlyIf is not string target || IsAnyApplied(target);

            bool IsAnyApplied(string name)
                => blocks.Any(other => !other.IsDefault
                    && other.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true && IsApplied(other));

            var matching = blocks.Where(block => !block.IsDefault).Where(IsApplied).ToHashSet();

            return blocks.Where(block => block.IsDefault
                ? (block.MergeMode == EnvironmentMergeMode.Always || matching.Count == 0) && IsApplied(block)
                : matching.Contains(block)).ToList();
        }

        private static string DescribeActive(IReadOnlyList<EnvironmentBlock> active)
        {
            var names = active.Where(block => !block.IsDefault).Select(block => "'" + block.DisplayName + "'").ToList();

            var text = names.Count > 0 ? string.Join(", ", names) : "none";

            return active.Any(block => block.IsDefault) ? $"{text} (+ default)" : text;
        }

        #endregion

        /// <summary>Builds the watcher over all resolved conditions of the given blocks (null
        /// when there are none). Its subscriptions do fallible platform work, so it is created
        /// before the previous generation is retired.</summary>
        private EnvironmentWatcher? CreateConditionWatcher(IReadOnlyList<EnvironmentBlock> blocks, TimeSpan debounce)
        {
            var conditions = blocks.SelectMany(block => block.Conditions).ToList();

            return conditions.Count > 0 ? new EnvironmentWatcher(this, conditions, debounce) : null;
        }

        public void Dispose()
        {
            // under the lock so it serializes with Reevaluate and Update: an in-flight one
            // either ran to completion before this, or waits on the lock and then sees
            // _disposed and skips. The watcher's timer stop does not block for a running
            // callback (that would deadlock against this lock) — the _disposed guard is what
            // makes it safe.
            lock (_lock)
            {
                if (_disposed)
                    return;

                _disposed = true;

                _conditionWatcher?.Dispose();

                _conditionScope?.Dispose();

                _reload.Dispose();
            }
        }
    }
}
