namespace MadWizard.Desomnia.LaunchDaemon.Configuration
{
    public class ProcessManagerConfig : Processes.Configuration.ProcessManagerConfig
    {
        /// <summary>
        /// Selects the macOS graphics accounting backend. Other platforms ignore this setting:
        /// their graphics counter has only one native implementation.
        /// </summary>
        public GraphicsMeasurementMode MeasureGPU { get; init; } = GraphicsMeasurementMode.Automatic;
    }

    public enum GraphicsMeasurementMode
    {
        /// <summary>Prefer per-process AGX accounting and fall back to coalition accounting.</summary>
        Automatic,

        /// <summary>Require the per-process AGX IORegistry counter.</summary>
        Process,

        /// <summary>Require the persistent counter shared by every process in a resource coalition.</summary>
        Coalition,
    }
}
