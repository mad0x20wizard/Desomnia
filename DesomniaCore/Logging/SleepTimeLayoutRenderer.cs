using MadWizard.Desomnia.Power.Watch;
using NLog;
using NLog.LayoutRenderers;
using System.Text;

namespace MadWizard.Desomnia.Logging
{
    [LayoutRenderer("sleep-duration")]
    public class SleepTimeLayoutRenderer : LayoutRenderer
    {
        public bool Archive { get; set; } = false;

        protected override void Append(StringBuilder sb, LogEventInfo logEvent)
        {
            if ( SleepWatch.Instance?.CollectSleepTime(Archive) is TimeSpan time)
            {
                if (time.Days > 0)
                    sb.Append(time.ToString("%d")).Append(" day(s), ");
                sb.Append(time.ToString(@"h\:mm")).Append(" h");
            }
            else
            {
                sb.Append("?");
            }

        }
    }
}
