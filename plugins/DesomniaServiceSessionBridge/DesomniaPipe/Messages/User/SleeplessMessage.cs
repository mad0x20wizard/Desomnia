using System;
using System.Collections.Generic;
using System.Text;

namespace MadWizard.Desomnia.Pipe.Messages
{
    public class SleeplessMessage : UserMessage
    {
        public SleeplessMessage()
        {
            SleeplessUntil = null;
        }

        public SleeplessMessage(bool sleepless = false)
        {
            if (sleepless)
                SleeplessUntil = DateTime.MaxValue;
            else
                SleeplessUntil =  null;
        }

        public SleeplessMessage(TimeSpan duration)
        {
            SleeplessUntil = DateTime.Now + duration;
        }

        public DateTime? SleeplessUntil { get; set; }
        public bool? SleeplessIfUsage { get; set; }
    }
}
