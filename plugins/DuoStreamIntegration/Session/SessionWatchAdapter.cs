using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Ressource.Events;
using MadWizard.Desomnia.Session;
using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Service.Duo.Session
{
    internal class SessionWatchAdapter(SessionMonitorConfig config) : IDisposable
    {
        public required DuoSessionMonitor   DuoSessionMonitor   { private get; init; }
        public required SessionMonitor      SessionMonitor      { private get; init; }

        bool IsConnectedTo(DuoInstance instance, ISession session)
        {
            if (instance.Settings.UserName == session.UserName)
            {
                if (session.IsRemoteConnected)
                {
                    return instance.Name == session.ClientName;
                }
                else if (session.IsHeadless)
                {
                    if (DuoSessionMonitor.TakeSnapshot().Count(i => i.Settings.UserName == instance.Settings.UserName) == 1)
                    {
                        return true; // only if the match is unambiguous
                    }
                }
            }

            return false;
        }

        private void MultiTrackIfConnected(IEnumerable<DuoInstance> instances, IEnumerable<SessionWatch> watches)
        {
            foreach (var watch in watches)
            {
                foreach (var instance in instances.Where(instance => IsConnectedTo(instance, watch.Session)))
                {
                    lock (instance)
                    {
                        instance.StartTracking(watch); break;
                    }
                }
            }
        }

        private void ClaimSessionWatch(DuoInstance instance, SessionWatch watch)
        {
            instance.Watch = (watch.Watch << instance.Info.Watch);

            watch.ApplyConfiguration(config, instance.Info with
            {
                Watch = WatchExpression.Yield,

                OnIdle = null // this will be handled by the Duo instance
            });

            if (watch.Session.IsRemoteConnected)
            {
                watch.WatchRemote = true; // Duo instances are technically remote sessions
            }
        }

        #region Lifecycle listeners
        internal void Attach()
        {
            SessionMonitor.TrackingStarted += SessionMonitor_TrackingStarted;
            DuoSessionMonitor.TrackingStarted += DuoSessionMonitor_TrackingStarted;
            SessionMonitor.InspectionFilter += SessionMonitor_InspectionFilter;
            DuoSessionMonitor.TrackingStopped += DuoSessionMonitor_TrackingStopped;
            SessionMonitor.TrackingStopped += SessionMonitor_TrackingStopped;
        }

        private void SessionMonitor_TrackingStarted(object? sender, InspectableEventArgs<SessionWatch> args)
        {
            MultiTrackIfConnected(DuoSessionMonitor.TakeSnapshot(), [args.Inspectable]);
        }

        private void DuoSessionMonitor_TrackingStarted(object? sender, InspectableEventArgs<DuoInstance> args)
        {
            args.Inspectable.TrackingStarted += DuoInstance_TrackingStarted;
            args.Inspectable.TrackingStopped += DuoInstance_TrackingStopped;

            MultiTrackIfConnected([args.Inspectable], SessionMonitor.TakeSnapshot());
        }

        private void DuoInstance_TrackingStarted(object? sender, InspectableEventArgs<Resource> args)
        {
            if (sender is DuoInstance instance && args.Inspectable is SessionWatch watch)
            {
                ClaimSessionWatch(instance, watch);
            }
        }

        // The Duo instance inspects its associated watch and supplies the final idle/usage result.
        private bool SessionMonitor_InspectionFilter(SessionWatch watch) => !watch.IsMonitoredBy<DuoInstance>();

        private void DuoInstance_TrackingStopped(object? sender, InspectableEventArgs<Resource> args)
        {
            // MAYBE clean up later
        }

        private void DuoSessionMonitor_TrackingStopped(object? sender, InspectableEventArgs<DuoInstance> args)
        {
            args.Inspectable.StopTracking<SessionWatch>();

            args.Inspectable.TrackingStopped -= DuoInstance_TrackingStopped;
            args.Inspectable.TrackingStarted -= DuoInstance_TrackingStarted;
        }

        private void SessionMonitor_TrackingStopped(object? sender, InspectableEventArgs<SessionWatch> args)
        {
            foreach (var instance in DuoSessionMonitor.TakeSnapshot())
            {
                instance.StopTracking(args.Inspectable);
            }
        }

        private void Detach()
        {
            SessionMonitor.TrackingStarted -= SessionMonitor_TrackingStarted;
            DuoSessionMonitor.TrackingStarted -= DuoSessionMonitor_TrackingStarted;
            SessionMonitor.InspectionFilter -= SessionMonitor_InspectionFilter;
            DuoSessionMonitor.TrackingStopped -= DuoSessionMonitor_TrackingStopped;
            SessionMonitor.TrackingStopped -= SessionMonitor_TrackingStopped;
        }
        #endregion

        void IDisposable.Dispose()
        {
            Detach();
        }
    }
}
