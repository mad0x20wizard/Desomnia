using Autofac;

namespace MadWizard.Desomnia.Processes.Manager.Metrics
{
    /// <summary>
    /// Couples a selected macOS GPU decorator's cache to one configured inspector without making
    /// either the core inspector or the process-monitor module aware of platform accounting.
    /// </summary>
    internal abstract class GraphicsInspectionBridge(IEnumerable<SystemUsageInspector> inspectors) : IStartable, IDisposable
    {
        private readonly SystemUsageInspector? _inspector = inspectors.SingleOrDefault();
        private IDisposable? _scope;

        protected abstract IDisposable BeginInspection();

        void IStartable.Start()
        {
            if (_inspector is null)
                return;

            _inspector.Inspecting += Inspector_Inspecting;
            _inspector.Inspected += Inspector_Inspected;
        }

        private void Inspector_Inspecting(object? sender, EventArgs args)
        {
            _scope?.Dispose();
            _scope = BeginInspection();
        }

        private void Inspector_Inspected(object? sender, EventArgs args)
        {
            _scope?.Dispose();
            _scope = null;
        }

        public void Dispose()
        {
            if (_inspector is not null)
            {
                _inspector.Inspecting -= Inspector_Inspecting;
                _inspector.Inspected -= Inspector_Inspected;
            }

            _scope?.Dispose();
            _scope = null;
        }
    }

    internal sealed class AGXGraphicsInspectionBridge(IEnumerable<SystemUsageInspector> inspectors) : GraphicsInspectionBridge(inspectors)
    {
        protected override IDisposable BeginInspection() => AGXGraphicsProcess.BeginInspection();
    }

    internal sealed class CoalitionGraphicsInspectionBridge(IEnumerable<SystemUsageInspector> inspectors) : GraphicsInspectionBridge(inspectors)
    {
        protected override IDisposable BeginInspection() => CoalitionGraphicsProcess.BeginInspection();
    }
}
