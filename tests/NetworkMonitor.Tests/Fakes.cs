using MadWizard.Desomnia.Network.Manager;
using System.Net;
using System.Net.NetworkInformation;
using MadWizard.Desomnia.Power.Manager;
using MadWizard.Desomnia.Power.Source;
using MadWizard.Desomnia.Network.Interface;

namespace MadWizard.Desomnia.Network.Tests
{
    internal sealed class FakePowerManager : IPowerManager
    {
        public PowerSource Source => PowerSource.Unknown;
        public event EventHandler? Suspended;
        public event EventHandler? ResumeSuspended;
        public void RaiseSuspended() => Suspended?.Invoke(this, EventArgs.Empty);
        public void RaiseResumed() => ResumeSuspended?.Invoke(this, EventArgs.Empty);
        public Task Suspend() => Task.CompletedTask;
        public Task Hibernate() => Task.CompletedTask;
        public Task Shutdown(TimeSpan? timeout = null, string? message = null, bool force = false) => Task.CompletedTask;
        public Task Reboot(TimeSpan? timeout = null, string? message = null, bool force = false) => Task.CompletedTask;
        public Task<IPowerRequest> CreateRequest(PowerRequestType type, string reason) => throw new NotSupportedException();
        public async IAsyncEnumerator<IPowerRequest> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
    /// <summary>Just enough <see cref="INetworkInterface"/> for matchers and planning —
    /// an id-keyed handle with settable observations and inert intent flags.</summary>
    internal class FakeNetworkInterface(string id) : INetworkInterface
    {
        public NetworkIdentity Identity { get; } = new(id);

        public string Name { get; init; } = id;

        public OperationalStatus Status { get; set; } = OperationalStatus.Up;

        public NetworkInterfaceType Type { get; set; } = NetworkInterfaceType.Ethernet;

        public PhysicalAddress PhysicalAddress { get; set; } = PhysicalAddress.None;

        public IReadOnlyList<UnicastIPAddressInformation> Addresses { get; set; } = [];

        public IReadOnlyList<GatewayIPAddressInformation> Gateways { get; set; } = [];

        public string? DNSSuffix { get; set; }

        public int? IPv6ScopeIndex { get; set; }

        public virtual string? SSID { get; set; }

        public bool FailDisabledRead { get; set; }
        public bool IsDisabled
        {
            get => FailDisabledRead ? throw new InvalidOperationException("Cannot read administrative state") : field;
            set;
        }
        public bool? ShouldBeDisabled
        {
            get;
            set
            {
                field = value;
                IsDisabled = value ?? false;
                if (value is not null) Status = IsDisabled ? OperationalStatus.Down : OperationalStatus.Up;
            }
        }

        public override string ToString() => Name;
    }

    /// <summary>A fixed roster of interfaces instead of a live OS enumeration.</summary>
    internal sealed class FakeNetworkInterfaceManager(params INetworkInterface[] interfaces) : INetworkInterfaceManager
    {
        private readonly List<INetworkInterface> _interfaces = [.. interfaces];

        private EventHandler? _changed;

        public INetworkInterface? this[NetworkIdentity identity] => _interfaces.FirstOrDefault(i => i.Identity == identity);

        public event EventHandler<INetworkInterface>? InterfaceAttached;
        public event EventHandler<INetworkInterface>? InterfaceDetached;

        public event EventHandler? Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        public bool HasChangedSubscribers => _changed is not null;

        public void RaiseChanged() => _changed?.Invoke(this, EventArgs.Empty);

        public void Attach(INetworkInterface @interface)
        {
            _interfaces.Add(@interface);

            InterfaceAttached?.Invoke(this, @interface);

            RaiseChanged();
        }

        public void Detach(INetworkInterface @interface)
        {
            _interfaces.Remove(@interface);

            InterfaceDetached?.Invoke(this, @interface);

            RaiseChanged();
        }

        IEnumerator<INetworkInterface> IEnumerable<INetworkInterface>.GetEnumerator() => _interfaces.GetEnumerator();
    }
}
