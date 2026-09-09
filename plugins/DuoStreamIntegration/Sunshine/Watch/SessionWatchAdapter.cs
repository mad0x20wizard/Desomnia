using Autofac;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Ressource.Events;
using MadWizard.Desomnia.Service.Duo.Manager;
using MadWizard.Desomnia.Session;
using MadWizard.Desomnia.Session.Configuration;

namespace MadWizard.Desomnia.Service.Duo.Sunshine.Watch
{
    internal class SessionWatchAdapter(SessionMonitorConfig config, SessionMonitor monitor, DuoManager manager) : IStartable, IDisposable
    {
        void IStartable.Start()
        {
            monitor.TrackingStarted += Monitor_TrackingStarted;
            monitor.InspectionFilter += Monitor_InspectionFilter;
            monitor.TrackingStopped += Monitor_TrackingStopped;

            manager.Started += Manager_Started;
        }

        private void Manager_Started(object? sender, DuoLifecycleEventArgs args)
        {
            foreach (var watch in monitor)
            {
                ClaimWatch(watch, args.Instances);
            }
        }

        private void Monitor_TrackingStarted(object? sender, InspectableEventArgs<SessionWatch> args)
        {
            ClaimWatch(args.Inspectable, manager);
        }

        private void ClaimWatch(SessionWatch watch, IEnumerable<DuoInstance> instances)
        {
            foreach (var instance in instances.Where(i => i.HasInitiated(watch.Session)))
            {
                if (!instance.Contains(watch))
                {
                    instance.Watch = watch.Watch << instance.Info.Watch;

                    watch.ApplyConfiguration(config, instance.Info with
                    {
                        Watch = WatchExpression.Yield,

                        OnIdle = null // this must only be handles by the DuoInstance
                    });

                    instance.StartTracking(watch);
                }

                break;
            }
        }

        /// <summary>
        /// This callback prevents the SessionMonitor from inspecting it's own
        /// SessionWatch resources, as long as any DuoInstance is associated with one of them.
        /// </summary>
        ///
        /// <returns>false = no inspection</returns>
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

            foreach (var instance in manager.Where(i => i.Contains(watch)))
            {
                instance.StopTracking(watch);
            }
        }

        void IDisposable.Dispose()
        {
            monitor.TrackingStarted -= Monitor_TrackingStarted;
            monitor.InspectionFilter -= Monitor_InspectionFilter;
            monitor.TrackingStopped -= Monitor_TrackingStopped;

            manager.Started -= Manager_Started;
        }
    }
}
