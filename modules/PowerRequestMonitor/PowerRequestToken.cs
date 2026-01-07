namespace MadWizard.Desomnia.PowerRequest
{
    internal class PowerRequestToken(string? name = null) : UsageToken
    {
        public override string ToString() => $"(({name ?? "PowerRequest"}))";
    }
}
