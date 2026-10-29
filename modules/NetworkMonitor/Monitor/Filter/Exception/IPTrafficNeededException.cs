namespace MadWizard.Desomnia.Network.Filter
{
    /// <summary>
    /// Requests IP unicast traffic after address resolution. The address identifies the
    /// watched destination to advertise, not the peer address in the filter context.
    /// </summary>
    public class IPTrafficNeededException : AdditionalDataNeededException
    {

    }
}
