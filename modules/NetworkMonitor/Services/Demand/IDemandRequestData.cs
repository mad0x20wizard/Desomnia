namespace MadWizard.Desomnia.Network.Demand
{
    public interface IDemandRequestData<T> // TODO: Implement DemandRequestData in request context
    {
        T Data { get; set; }
    }
}
