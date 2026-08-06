using MadWizard.Desomnia.Network.Watch;
using MadWizard.Desomnia.Service.Duo.Manager;

namespace MadWizard.Desomnia.Service.Duo.Sunshine
{
    internal class SunshineServiceAdapter
    {
        readonly Dictionary<DuoInstance, NetworkServiceWatch> _watches = [];

        protected void RegisterWatch(DuoInstance instance, NetworkServiceWatch watch)
        {
            try
            {
                watch.Demand += instance.NetworkServiceWatch_Demand;

                if (instance.Info.PreventIdleIfStreaming ?? true)
                {
                    instance.StartTracking(watch);
                }
                else
                {
                    instance.Inspected += Instance_Inspected;
                }

                _watches[instance] = watch;
            }
            catch
            {
                try { UnregisterWatch(instance, watch); } catch { } throw; // clean up
            }
        }

        private void Instance_Inspected(object? sender, TimeSpan interval)
        {
            if (sender is DuoInstance instance && _watches.TryGetValue(instance, out var watch))
            {
                watch.Inspect(interval);
            }
        }

        protected void UnregisterWatch(DuoInstance instance, NetworkServiceWatch watch)
        {
            if (_watches.Remove(instance))
            {
                watch.Demand -= instance.NetworkServiceWatch_Demand;

                instance.Inspected -= Instance_Inspected;

                instance.StopTracking(watch);
            }
        }
    }
}