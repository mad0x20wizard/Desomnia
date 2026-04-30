namespace MadWizard.Desomnia.Service.Duo.Manager
{
    internal enum DuoEventID
    {
        // The available ranges are:
        // 1000-1028
        // 1100-1113
        // 1130

        // Regular events
        ServiceStarted = 1000,
        ServiceStopped,
        ServiceError,
        InstanceStarted,
        InstanceStopped,
        InstanceError,
        ProcessStarted,
        ProcessError,
        FeatureConfigurationChanged,
        Suspending,
        Resuming,
        DisplaySettingsChanged,

        // Debug output
        DebugChannel = 1130
    }
}
