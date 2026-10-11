using MadWizard.Desomnia.Network.Neighborhood;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.NetworkInformation;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace MadWizard.Desomnia.Network.SleepProxy.Registration
{
    internal class SleepProxyLease : IDisposable
    {
        private ScheduledTimer _timer;

        readonly Stack<IDisposable> _disposables = [];

        public required SleepProxyRegistration Registration { get; init; }

        public TimeSpan Duration { get; init; }
        public DateTime GrantedSince { get; } = DateTime.Now;
        public DateTime GrantedUntil => GrantedSince + Duration;

        public event EventHandler<SleepProxyLeaseEndEventArgs>? Ended;
        public event EventHandler? Disposed;

        public SleepProxyLease(TimeSpan duration)
        {
            Duration = duration;

            _timer = new ScheduledTimer(GrantedUntil);
            _timer.Elapsed += (sender, args) => Stop(SleepProxyLeaseEndReason.Expired);
            _timer.Start();
        }

        public void AddInstanceForDisposal(IDisposable instance)
        {
            _disposables.Push(instance);
        }

        internal void Stop(SleepProxyLeaseEndReason reason, TimeSpan? timeout = null)
        {
            _timer?.Stop();

            Ended?.Invoke(this, new(reason) { Timeout = timeout });
            Ended = null;
        }

        public void Dispose()
        {
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null!;

                while (_disposables.Count > 0)
                {
                    var item = _disposables.Pop();

                    try
                    {
                        item.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        // we can safely ignore this here
                    }
                }

                Disposed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    class SleepProxyPhysicalAddressLease(NetworkHost host, PhysicalAddress adr) : IDisposable
    {
        void IDisposable.Dispose()
        {
            if (adr.Equals(host.PhysicalAddress))
            {
                host.PhysicalAddress = null;
            }
        }
    }

    class SleepProxyAddressLease(ILogger logger, NetworkHost host, IPAddress ip) : IDisposable
    {
        void IDisposable.Dispose()
        {
            if (host.RemoveAddress(ip))
            {
                logger.LogHostAddressRemoved(host, ip);
            }
        }
    }

    class SleepProxyDynamicHostRangeLease(ILogger logger) : IDisposable
    {
        void IDisposable.Dispose()
        {
            // TODO implement
        }
    }

    class SleepProxyLeaseEndEventArgs(SleepProxyLeaseEndReason reason) : EventArgs
    {
        public SleepProxyLeaseEndReason Reason => reason;

        public TimeSpan? Timeout { get; init; }

        public bool HasExpired => Reason == SleepProxyLeaseEndReason.Expired;
        public bool HasFailed => Reason == SleepProxyLeaseEndReason.Failed;
    }

    enum SleepProxyLeaseEndReason
    {
        Failed,

        HostStarted,

        Expired
    }
}
