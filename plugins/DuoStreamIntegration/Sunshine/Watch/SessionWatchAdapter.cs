using Autofac;
using MadWizard.Desomnia.Ressource.Events;
using MadWizard.Desomnia.Service.Duo.Manager;
using MadWizard.Desomnia.Session;
using MadWizard.Desomnia.Session.Configuration;

namespace MadWizard.Desomnia.Service.Duo.Sunshine.Watch
{
    internal class SessionWatchAdapter(SessionMonitorConfig config, SessionMonitor monitor, DuoManager manager) : IStartable
    {
        void IStartable.Start()
        {
            monitor.TrackingStarted += Monitor_TrackingStarted;
            monitor.InspectionFilter += Monitor_InspectionFilter;
            monitor.TrackingStopped += Monitor_TrackingStopped;

            manager.Started += Manager_Started;
        }

        private void Manager_Started(object? sender, EventArgs e)
        {
            foreach (var watch in monitor)
            {
                ClaimWatch(watch);
            }
        }

        private void Monitor_TrackingStarted(object? sender, InspectableEventArgs<SessionWatch> args)
        {
            ClaimWatch(args.Inspectable);
        }

        private void ClaimWatch(SessionWatch watch)
        {
            foreach (var instance in manager)
            {
                if (instance.HasInitiated(watch.Session))
                {
                    watch.ApplyConfiguration(config, instance.Info);

                    instance.StartTracking(watch);
                }
            }
        }

        private bool Monitor_InspectionFilter(SessionWatch watch)
        {
            foreach (var instance in manager)
            {
                if (instance.Contains(watch))
                {
                    return false;
                }
            }

            return true;
        }

        private void Monitor_TrackingStopped(object? sender, InspectableEventArgs<SessionWatch> args)
        {
            var watch = args.Inspectable;

            foreach (var instance in manager)
            {
                if (instance.Contains(watch))
                {
                    instance.StopTracking(watch);
                }
            }
        }
    }
}
