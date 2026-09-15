using MadWizard.Desomnia.Configuration.Converter;
using System.ComponentModel;
using System.Text;

namespace MadWizard.Desomnia.Configuration
{
    public class SystemMonitorConfig
    {
        public TimeSpan?            Timeout             { get; set; }

        /// <summary>Additionally keep the display from idle-sleeping while sleepless.</summary>
        public bool                 KeepDisplayAwake    { get; set; } = false;

        public DelayedActionInfo?   OnIdle              { get; set; }
        public ActionInfo?          OnUsage             { get; set; }
        public ActionInfo?          OnSuspend           { get; set; }
        public DelayedActionInfo?   OnSuspendTimeout    { get; set; }
        public DelayedActionInfo?   OnResume            { get; set; }

        static SystemMonitorConfig()
        {
            TypeDescriptor.AddAttributes(typeof(Encoding), new TypeConverterAttribute(typeof(EncodingConverter)));
        }
    }
}
