using MadWizard.Desomnia.Application;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.ServiceProcess;

namespace MadWizard.Desomnia.Service
{
    public class WindowsService : WindowsServiceLifetime
    {
        public required ILogger<WindowsService> Logger { private get; init; }

        public WindowsService(
            ILoggerFactory logging, 
            IHostEnvironment environment, 
            IHostApplicationLifetime lifetime, 
            IOptions<HostOptions> options)
                : base(environment, lifetime, logging, options)
        {
            lifetime.ApplicationStopping.Register(() => RequestAdditionalTime(options.Value.ShutdownTimeout));
            lifetime.ApplicationStopped .Register(() => ReportFatalExitCode());

            CanHandlePowerEvent = true;
            CanHandleSessionChangeEvent = true;
            CanShutdown = true;
        }

        internal event EventHandler<PowerBroadcastStatus>? PowerStatusChanged;
        internal event EventHandler<SessionChangeDescription>? SessionChanged;

        #region Service lifecycle
        protected override void OnStart(string[] args)
        {
            new WindowsServiceParameters().Parse(args).Invoke();

            base.OnStart(args);

            Logger.LogInformation("Service started");
        }

        protected override void OnStop()
        {
            Logger.LogInformation("Service is stopping...");

            base.OnStop();

            Logger.LogInformation("Service stopped");
        }

        protected override void OnShutdown()
        {
            Logger.LogInformation("System is shutting down - stopping...");

            base.OnShutdown();

            Logger.LogInformation("Service stopped");
        }
        #endregion

        #region Power/Session events
        protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
        {
            PowerStatusChanged?.Invoke(this, powerStatus);

            return true; // TODO Query Suspended?
        }
        protected override void OnSessionChange(SessionChangeDescription changeDescription)
        {
            SessionChanged?.Invoke(this, changeDescription);
        }
        #endregion

        /// <summary>
        /// A fatal configuration error sets a non-zero exit code (see the DesomniaHost loop)
        /// before it stops the application; report it as the service's exit code, so the SCM
        /// sees a FAILED stop and its recovery actions restart the service (self-heal).
        /// </summary>
        private void ReportFatalExitCode()
        {
            if (Environment.ExitCode != 0)
            {
                ExitCode = Environment.ExitCode;
            }
        }
    }
}