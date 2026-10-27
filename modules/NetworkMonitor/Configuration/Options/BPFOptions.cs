namespace MadWizard.Desomnia.Network.Configuration.Options
{
    public readonly struct BPFOptions
    {
        /// <summary>Null generates a filter from traffic requests; other values are literal BPF expressions.</summary>
        public string? Expression { get; init; }

        /// <summary>A hard instruction budget; null uses the platform limit when known.</summary>
        public int? InstructionLimit
        {
            get; init
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(InstructionLimit), "The BPF instruction limit must be positive.");

                field = value;
            }
        }
    }
}
