namespace MadWizard.Desomnia.Configuration
{
    public class ActionGroupConfig
    {
        public IList<ActionConfig> Action { get; private set; } = [];
    }

    public class ActionConfig
    {
        public required string Name { get; set; }

        public required TimeSpan Delay { get; set; }

        public required NamedAction Text { get; set; }
    }
}
