namespace MadWizard.Desomnia.Extensions.EventHandler
{
    public delegate void ReadonlyEventHandler<TEventArgs>(object? sender, in TEventArgs e)
        where TEventArgs : allows ref struct;

    public delegate void ReadonlyEventHandler<in TSender, TEventArgs>(TSender sender, in TEventArgs e)
        where TSender : allows ref struct
        where TEventArgs : allows ref struct;
}
