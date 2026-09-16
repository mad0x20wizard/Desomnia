namespace MadWizard.Desomnia.Events
{
    [AttributeUsage(AttributeTargets.Method)]
    public class ActionHandlerAttribute(string name) : Attribute
    {
        public string Name => name;

        public bool Concurrent { get; set; } = false;

        /// <summary>
        /// Run the handler independently of the triggering event. Errors are still
        /// routed through the action error pipeline, but cannot propagate to the
        /// trigger caller after it has returned.
        /// </summary>
        public bool Detached { get; set; } = false;
    }
}
