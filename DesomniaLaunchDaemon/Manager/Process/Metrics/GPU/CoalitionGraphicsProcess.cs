using Autofac.Features.Decorators;
using MadWizard.Desomnia.LaunchDaemon.Native;

namespace MadWizard.Desomnia.Processes.Manager.Metrics
{
    /// <summary>Persistent GPU accounting shared by every process in a macOS resource coalition.</summary>
    internal sealed class CoalitionGraphicsProcess(IProcess process, IDecoratorContext context) : ProcessDecorator(process, context)
    {
        [ThreadStatic]
        private static Dictionary<ulong, TimeSpan?>? _inspection;

        private ulong? _coalition;

        public override TimeSpan? GraphicsProcessorTime
        {
            get
            {
                _coalition ??= Coalitions.ResourceCoalitionOf(Id);

                // Processes launched by the daemon share its own coalition with unrelated siblings,
                // so that ledger cannot honestly be attributed to any one of them.
                if (_coalition is not ulong coalition || coalition == Coalitions.Own)
                    return null;

                if (_inspection is null)
                    return Coalitions.GraphicsTimeOf(coalition);

                if (!_inspection.TryGetValue(coalition, out TimeSpan? value))
                    _inspection[coalition] = value = Coalitions.GraphicsTimeOf(coalition);

                return value;
            }
        }

        internal static bool Probe() =>
            Coalitions.Own is ulong own && Coalitions.GraphicsTimeOf(own) is not null;

        internal static IDisposable BeginInspection()
        {
            _inspection = [];

            return new InspectionScope();
        }

        private sealed class InspectionScope : IDisposable
        {
            public void Dispose() => _inspection = null;
        }
    }
}
