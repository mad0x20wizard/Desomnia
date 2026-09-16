namespace MadWizard.Desomnia
{
    public abstract class UsageToken
    {
        public readonly ISet<UsageToken> Tokens = new HashSet<UsageToken>();
    }
}
