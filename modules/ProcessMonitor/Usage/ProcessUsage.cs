namespace MadWizard.Desomnia.Process
{
    public class ProcessUsage(string name) : UsageToken
    {
        public string Name => name;

        public double? Usage { get; private init; }
        public TimeSpan? Time { get; private init; }

        public ProcessUsage(string name, double usage) : this(name)
        {
            this.Usage = usage;
        }

        public ProcessUsage(string name, TimeSpan time) : this(name)
        {
            this.Time = time;
        }

        public string ToUsage()
        {
            if (Usage is double usage)
                return $":{usage * 100:0.0}%";
            else if (Time is TimeSpan time)
                return $":{time}";
            else
                return "";
        }

        public override string ToString() => "{" + Name + ToUsage() + "}";
    }
}
