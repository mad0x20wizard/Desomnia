using Autofac;
using MadWizard.Desomnia.Power.Manager;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MadWizard.Desomnia.Power
{
    public class SleepWatch(IPowerManager power) : IStartable
    {
        public required ILogger<SleepWatch> Logger { private get; set; }

        private static Stopwatch ClockSleep { get; set; } = new Stopwatch();
        private Stopwatch ClockNap { get; set; } = new Stopwatch();

        public void Start()
        {
            power.Suspended += PowerManager_Suspend;
            power.ResumeSuspended += PowerManager_ResumeSuspend;
        }

        private void PowerManager_Suspend(object? sender, EventArgs e)
        {
            ClockNap.Restart();
            ClockSleep.Start();
        }

        private void PowerManager_ResumeSuspend(object? sender, EventArgs e)
        {
            ClockSleep.Stop();
            ClockNap.Stop();

            Logger.LogInformation($"{ClockNap.Elapsed:h':'mm} h");
        }

        internal static TimeSpan CollectSleepTime()
        {
            try
            {
                return ClockSleep.Elapsed;
            }
            finally
            {
                ClockSleep.Reset();
            }
        }
    }
}
