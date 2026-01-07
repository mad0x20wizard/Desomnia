using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.Service
{
    public class WindowsService : WindowsServiceLifetime
    {
        public required ILogger<WindowsService> Logger { private get; init; }

        public WindowsService(IHostEnvironment environment, IHostApplicationLifetime lifetime, ILoggerFactory logging, IOptions<HostOptions> options)
            : base(environment, lifetime, logging, options)
        {
            CanHandlePowerEvent = true;
            CanHandleSessionChangeEvent = true;
            CanShutdown = true;
        }

        internal event EventHandler<PowerBroadcastStatus>? PowerStatusChanged;
        internal event EventHandler<SessionChangeDescription>? SessionChanged;

        protected override void OnStart(string[] args)
        {
            foreach (string arg in args)
            {
                if (arg.StartsWith("/WaitForDebugger"))
                {
                    Helper.Debugger.UntilAttached().Wait();
                }
            }

            base.OnStart(args);

            Logger.LogInformation("Startup complete");
        }

        protected override void OnStop()
        {
            Logger.LogInformation("Shutdown requested...");

            base.OnStop();

            Logger.LogInformation("Shutdown complete");
        }

        protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
        {
            PowerStatusChanged?.Invoke(this, powerStatus);

            return true; // TODO Query Suspended?
        }
        protected override void OnSessionChange(SessionChangeDescription changeDescription)
        {
            SessionChanged?.Invoke(this, changeDescription);
        }

        #region ServiceStatus
        private void ReportServiceStatus(ServiceState state, TimeSpan waitHint)
        {
            ServiceStatus status = new()
            {
                dwCurrentState = state,
                dwWaitHint = (int)waitHint.TotalMilliseconds
            };

            SetServiceStatus(this.ServiceHandle, ref status);
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetServiceStatus(nint handle, ref ServiceStatus serviceStatus);

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceStatus
        {
            public int dwServiceType;
            public ServiceState dwCurrentState;
            public int dwControlsAccepted;
            public int dwWin32ExitCode;
            public int dwServiceSpecificExitCode;
            public int dwCheckPoint;
            public int dwWaitHint;
        }

        private enum ServiceState
        {
            SERVICE_STOPPED = 1,
            SERVICE_START_PENDING = 2,
            SERVICE_STOP_PENDING = 3,
            SERVICE_RUNNING = 4,
            SERVICE_CONTINUE_PENDING = 5,
            SERVICE_PAUSE_PENDING = 6,
            SERVICE_PAUSED = 7,
        }
        #endregion
    }
}