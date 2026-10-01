using MadWizard.Desomnia.Configuration;

namespace MadWizard.Desomnia.Service.Duo.Configuration
{
    public class DuoSessionMonitorConfig
    {
        public required string ServiceName                      { get; set; } = "DuoService";

        public TimeSpan PollInterval                            { get; set; } = TimeSpan.FromSeconds(2); // deprecated

        public WatchMode WatchMode                              { get; set; } = WatchMode.Auto;

        public ActionInfo? OnUsage                              { get; set; }
        public DelayedActionInfo? OnIdle                        { get; set; }

        public ActionInfo? OnInstanceDemand                     { get; set; }
        public DelayedActionInfo? OnInstanceUsage               { get; set; }
        public DelayedActionInfo? OnInstanceIdle                { get; set; }
        public ScheduledActionInfo? OnInstanceLogin             { get; set; }
        public ScheduledActionInfo? OnInstanceStart             { get; set; }
        public ScheduledActionInfo? OnInstanceStop              { get; set; }
        public ScheduledActionInfo? OnInstanceLogout            { get; set; }

        public bool WatchStreamTraffic                          { get; set; } = true;

        public TransmissionThreshold? MinInstanceStreamTraffic  { get; set; }

        public IList<DuoInstanceWatchInfo> Instance { get; private set; } = [];

        internal DuoInstanceWatchInfo? this[string name] => Instance.FirstOrDefault(i => i.Name == name);

        internal IEnumerable<WatchMode> AllowedWatchModes()
        {
            List<WatchMode> modes = [];
            if (WatchMode.HasFlag(WatchMode.Registry))
                modes.Add(WatchMode.Registry);
            if (WatchMode.HasFlag(WatchMode.EventLog))
                modes.Add(WatchMode.EventLog);
            if (WatchMode.HasFlag(WatchMode.Polling))
                modes.Add(WatchMode.Polling);

            if (modes.Count > 0)
                return modes;

            return [WatchMode.Registry, WatchMode.EventLog, WatchMode.Polling]; // Auto
        }
    }

    [Flags]
    public enum WatchMode
    {
        Auto        = 0,

        Polling     = 1 << 1,
        EventLog    = 1 << 2,
        Registry    = 1 << 3,

        Listener    = 1 << 11,
        Capture     = 1 << 12,
    }
}
