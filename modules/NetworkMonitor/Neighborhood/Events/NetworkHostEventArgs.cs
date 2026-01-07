namespace MadWizard.Desomnia.Network.Neighborhood.Events
{
    public class NetworkHostEventArgs(NetworkHost host) : EventArgs
    {
        public NetworkHost Host => host;
    }
}
