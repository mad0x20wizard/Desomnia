namespace MadWizard.Desomnia.Process
{
    public class ProcessUsage(string name, double usage) : UsageToken
    {
        public string Name => name;
        public double Usage => usage;
        
        public string? UserName { get; set; }

        public override string ToString() => '{' + (UserName != null ? $@"{UserName}\" : string.Empty) + $"{Name}:{Usage:0.00}%" + '}';
    }
}
