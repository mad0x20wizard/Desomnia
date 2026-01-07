namespace MadWizard.Desomnia
{
    public delegate Task EventInvocation(Event data);

    public delegate Task EventInvocation<T>(T data) where T : Event;
}
