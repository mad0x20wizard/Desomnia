using MadWizard.Desomnia.Environments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.ExceptionServices;

namespace MadWizard.Desomnia
{
    /// <summary>
    /// The host <see cref="ApplicationBuilder.Build"/> returns: it wraps the persistent
    /// Microsoft.Extensions host and encapsulates the configuration rebuild loop — each
    /// iteration builds a fresh inner application host and runs it until the
    /// <see cref="EnvironmentMonitor"/>'s reload signal (a file change or a condition
    /// change) or a process stop. The loop lives HERE, outside any container, so a fatal
    /// error — an invalid first build, a bad configuration edit, a changed persistent
    /// configuration — escapes <see cref="Run"/> back to the process entry point (which
    /// logs it and exits non-zero; the service manager restarts the application).
    /// A reload re-enters the loop in-process and never touches the persistent lifetime;
    /// an inner host that stops WITHOUT a reload pending (a deliberate stop such as the
    /// promiscuous-mode mutex) ends the whole process normally.
    /// </summary>
    public sealed class ApplicationHost : IHost
    {
        readonly IHost _host;
        readonly ApplicationBuilder _builder;

        readonly EnvironmentMonitor _monitor;
        readonly ConfigurationPipeline _pipeline;
        readonly IHostApplicationLifetime _lifetime;
        readonly ILogger<ApplicationHost> _logger;

        Task? _loop;
        Exception? _fatal;

        internal ApplicationHost(IHost host, ApplicationBuilder builder)
        {
            _host = host;
            _builder = builder;

            _monitor    = host.Services.GetRequiredService<EnvironmentMonitor>();
            _pipeline   = host.Services.GetRequiredService<ConfigurationPipeline>();
            _lifetime   = host.Services.GetRequiredService<IHostApplicationLifetime>();
            _logger     = host.Services.GetRequiredService<ILogger<ApplicationHost>>();
        }

        /// <summary>The persistent host's services (the machine-lifetime Autofac container).</summary>
        public IServiceProvider Services => _host.Services;

        public void Run() => RunAsync().GetAwaiter().GetResult();

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await ((IHost)this).StartAsync(cancellationToken);

                // wait until something asks the application to stop: the lifetime (console,
                // SCM, systemd) or the loop itself (fatal, or a deliberate inner stop)
                TaskCompletionSource stopping = new(TaskCreationOptions.RunContinuationsAsynchronously);

                using var stop = _lifetime.ApplicationStopping.Register(stopping.SetResult);
                using var external = cancellationToken.Register(_lifetime.StopApplication);

                await stopping.Task;
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "RunAsync() failed.");
            }
            finally
            {
                await ((IHost)this).StopAsync(CancellationToken.None);

                Dispose();
            }

            if (_fatal is Exception fatal)
                ExceptionDispatchInfo.Capture(fatal).Throw();
        }

        /// <summary>
        /// Starts the persistent host (which owns the real process lifetime — the
        /// Windows service or the console lifetime) and the rebuild loop.
        /// </summary>
        async Task IHost.StartAsync(CancellationToken cancellationToken)
        {
            await _host.StartAsync(cancellationToken);

            _loop = RunLoopAsync(_lifetime.ApplicationStopping);
        }

        private async Task RunLoopAsync(CancellationToken stopping)
        {
            try
            {
                while (!stopping.IsCancellationRequested)
                {
                    // a fatal configuration change may have been recorded while no run was active
                    _pipeline.ThrowIfFailed();

                    // BuildApplication arms the monitor's reload token for this run, so a change
                    // that lands even during the build cancels the token below. A build failure
                    // is fatal by design: a bad edit exits the application (self-heal by restart).
                    IHost application = _builder.BuildApplication();

                    // re-check AFTER the build armed the fresh token: a failure recorded between
                    // the check above and ArmReload cancelled only the PREVIOUS token, which the
                    // arming just discarded - without this, the fatal signal would be lost and
                    // the process would keep running the stale configuration forever
                    _pipeline.ThrowIfFailed();

                    bool reloadRequested;

                    using (application)
                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(stopping, _monitor.ReloadToken))
                    {
                        try
                        {
                            if (!linked.IsCancellationRequested)
                                await application.RunAsync(linked.Token);
                        }
                        catch (OperationCanceledException) when (linked.IsCancellationRequested)
                        {
                            // the normal end of a run: a reload or stop, possibly during startup
                        }
                        catch (Exception ex) when (linked.IsCancellationRequested)
                        {
                            // the run was ending anyway and the inner host faulted while draining
                            // (e.g. a plugin's StopAsync threw). Never fatal: a reload rebuilds.
                            _logger.LogWarning(ex, "The application host faulted while stopping for a reload or shutdown; continuing.");
                        }

                        // capture BEFORE the inner container disposal widens the window: an
                        // in-flight change on a watcher thread must not turn a deliberate
                        // inner stop into a rebuild
                        reloadRequested = _monitor.ReloadToken.IsCancellationRequested;
                    }

                    // a deliberate inner stop (e.g. the promiscuous-mode mutex) leaves the token
                    // uncancelled and ends the loop -> the whole process stops, successfully -
                    // unless a fatal landed after the capture, which must not exit with code 0
                    if (!reloadRequested)
                    {
                        _pipeline.ThrowIfFailed();

                        return;
                    }
                }
            }
            catch (Exception ex) when (!stopping.IsCancellationRequested)
            {
                // an invalid configuration (the first build, a bad edit, a changed persistent
                // configuration) or an unexpected loop fault: exit with an error code and let
                // the service manager restart the application
                _fatal = ex;

                Environment.ExitCode = 1;

                _logger.LogCritical(ex, "The application cannot continue and will exit.");
            }
            finally
            {
                if (!stopping.IsCancellationRequested)
                    _lifetime.StopApplication();
            }
        }

        /// <summary>Stops in teardown order: the loop drains the current inner host first,
        /// and only then does the persistent host stop and its container dispose — restoring
        /// the OS state the machine-lifetime services hold.</summary>
        async Task IHost.StopAsync(CancellationToken cancellationToken)
        {
            _lifetime.StopApplication();

            if (_loop is Task loop)
            {
                var timeout = _host.Services.GetService<IOptions<HostOptions>>()?.Value.ShutdownTimeout
                    ?? TimeSpan.FromSeconds(30);

                try
                {
                    await loop.WaitAsync(timeout, cancellationToken);
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("The application loop did not drain within the shutdown timeout; stopping anyway.");
                }
            }

            await _host.StopAsync(cancellationToken);
        }

        /// <summary>Disposes the persistent host — and with it the Autofac container,
        /// restoring the OS state the machine-lifetime services hold. Idempotent.</summary>
        public void Dispose() => _host.Dispose();
    }
}
