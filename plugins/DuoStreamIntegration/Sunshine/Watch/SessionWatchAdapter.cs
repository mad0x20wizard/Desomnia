using Autofac;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Ressource.Events;
using MadWizard.Desomnia.Session;
using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Service.Duo.Sunshine.Watch
{
    internal class SessionWatchAdapter(SessionMonitorConfig config) : IDisposable
    {
        public required DuoSessionMonitor   DuoSessionMonitor   { private get; init; }
        public required SessionMonitor      SessionMonitor      { private get; init; }

        static bool IsConnectedTo(DuoInstance instance, ISession session)
        {
            return instance.Name == session.ClientName && instance.Settings.UserName == session.UserName;
        }

        internal void Attach()
        {
            SessionMonitor.TrackingStarted += SessionMonitor_TrackingStarted;
            SessionMonitor.InspectionFilter += SessionMonitor_InspectionFilter;
            SessionMonitor.TrackingStopped += SessionMonitor_TrackingStopped;

            DuoSessionMonitor.TrackingStarted += DuoSessionMonitor_TrackingStarted;
            DuoSessionMonitor.TrackingStopped += DuoSessionMonitor_TrackingStopped;
        }

        private void DuoSessionMonitor_TrackingStarted(object? sender, InspectableEventArgs<DuoInstance> args)
        {
            MaybeClaimWatch(SessionMonitor.TakeSnapshot(), [args.Inspectable]);
        }

        private void SessionMonitor_TrackingStarted(object? sender, InspectableEventArgs<SessionWatch> args)
        {
            MaybeClaimWatch([args.Inspectable], DuoSessionMonitor.TakeSnapshot());
        }

        private void MaybeClaimWatch(IEnumerable<SessionWatch> watches, IEnumerable<DuoInstance> instances)
        {
            foreach (var watch in watches) foreach (var instance in instances.Where(instance => IsConnectedTo(instance, watch.Session)))
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
        private bool SessionMonitor_InspectionFilter(SessionWatch watch) => !watch.IsMonitoredBy<DuoInstance>();

        private void SessionMonitor_TrackingStopped(object? sender, InspectableEventArgs<SessionWatch> args)
        {
            var watch = args.Inspectable;

            foreach (var instance in DuoSessionMonitor.TakeSnapshot())
            {
                instance.StopTracking(watch);
            }
        }

        private void DuoSessionMonitor_TrackingStopped(object? sender, InspectableEventArgs<DuoInstance> args)
        {
            var instance = args.Inspectable;

            foreach (var watch in instance.OfType<SessionWatch>())
            {
                instance.StopTracking(watch);
            }
        }

        private void Detach()
        {
            DuoSessionMonitor.TrackingStarted -= DuoSessionMonitor_TrackingStarted;
            DuoSessionMonitor.TrackingStopped -= DuoSessionMonitor_TrackingStopped;

            SessionMonitor.TrackingStarted -= SessionMonitor_TrackingStarted;
            SessionMonitor.InspectionFilter -= SessionMonitor_InspectionFilter;
            SessionMonitor.TrackingStopped -= SessionMonitor_TrackingStopped;
        }

        void IDisposable.Dispose()
        {
            Detach();
        }
    }
}
