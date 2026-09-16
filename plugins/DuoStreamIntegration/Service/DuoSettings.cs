using System.Collections.Immutable;

namespace MadWizard.Desomnia.Service.Duo
{
    public readonly struct DuoSettings
    {
        public required uint Port { get; init; }

        public required ImmutableArray<InstanceSettings> Instances { get; init; }
    }

    public readonly struct InstanceSettings
    {
        public required string Name { get; init; }
        public required string DisplayName { get; init; }
        public required ushort Port { get; init; }
        public required string UserName { get; init; }
        public required bool IsSandboxed { get; init; }
    }
}
