namespace MadWizard.Desomnia.Power.Source
{
    /// <summary>
    /// Standalone reader of the system's current power source. Implemented by the
    /// platform projects on the same logic that backs <see cref="Manager.IPowerManager.Source"/>,
    /// but usable outside the application container (e.g. by the EnvironmentMonitor,
    /// which lives outside the normal application lifecycle).
    /// </summary>
    public interface IPowerSource
    {
        PowerSource Source { get; }

        event EventHandler? SourceChanged;
    }
}
