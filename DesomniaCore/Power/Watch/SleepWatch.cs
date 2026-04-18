using Autofac;
using MadWizard.Desomnia.Power.Manager;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MadWizard.Desomnia.Power.Watch
{
    public class SleepWatch(IPowerManager power) : IStartable
    {
        public required ILogger<SleepWatch> Logger { private get; set; }

        public static SleepWatch? Instance { get; private set; }

        private DateTime LastSleepTime { get; set; } = DateTime.Now;
        private TimeSpan SleepDuration { get; set; } = TimeSpan.Zero;

        private Stopwatch ClockSleep { get; set; } = new Stopwatch();

        void IStartable.Start()
        {
            power.Suspended += PowerManager_Suspend;
            power.ResumeSuspended += PowerManager_ResumeSuspend;

            Instance = this;
        }

        private void PowerManager_Suspend(object? sender, EventArgs e)
        {
            LastSleepTime = DateTime.Now;

            ClockSleep.Restart();
        }

        private void PowerManager_ResumeSuspend(object? sender, EventArgs e)
        {
            ClockSleep.Stop();

            SleepDuration += ClockSleep.Elapsed;

            Logger.LogInformation($"{ClockSleep.Elapsed:h':'mm} h");
        }

        internal TimeSpan CollectSleepTime(bool preserveToday = false)
        {
            TimeSpan preserve = TimeSpan.Zero;
            if (preserveToday && LastSleepTime.Date != DateTime.Today)
                preserve = DateTime.Now.TimeOfDay;

            try
            {
                return SleepDuration - preserve;
            }
            finally
            {
                SleepDuration = preserve;
            }
        }
    }
}
