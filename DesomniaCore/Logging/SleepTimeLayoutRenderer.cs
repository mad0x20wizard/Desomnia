using MadWizard.Desomnia.Power;
using NLog;
using NLog.LayoutRenderers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.Logging
{
    [LayoutRenderer("sleep-duration")]
    public class SleepTimeLayoutRenderer : LayoutRenderer
    {
        protected override void Append(StringBuilder sb, LogEventInfo logEvent)
        {
            var time = SleepWatch.CollectSleepTime();

            if (time.Days > 0)
                sb.Append(time.ToString("%d")).Append(" day(s), ");
            sb.Append(time.ToString(@"h\:mm")).Append(" h");
        }
    }
}
