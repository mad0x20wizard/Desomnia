namespace MadWizard.Desomnia
{
    [AttributeUsage(AttributeTargets.Method)]
    public class ActionHandlerAttribute(string name) : Attribute
    {
        public string Name => name;

        public bool Concurrent { get; set; } = false;
    }
}
