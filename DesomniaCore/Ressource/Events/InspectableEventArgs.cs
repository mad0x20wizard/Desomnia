namespace MadWizard.Desomnia.Ressource.Events
{
    public class InspectableEventArgs<T>(T inspectable) : EventArgs where T : IInspectable
    {
        public T Inspectable { get; } = inspectable;
    }
}
