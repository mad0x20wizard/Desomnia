using MadWizard.Desomnia.Environments;
using MadWizard.Desomnia.Network.Interface;
using MadWizard.Desomnia.Network.Manager;

namespace MadWizard.Desomnia.Network.Environments
{
    /// <summary>
    /// Base for environment conditions that evaluate the local network interfaces.
    /// Subscribes to the manager's change event only while someone listens.
    /// Public because the platform hosts register derived conditions of their own
    /// (e.g. the Windows "ssid"); the <see cref="INetworkInterfaceManager"/> and the
    /// <see cref="InterfaceMatcher"/> arrive constructor-injected from the persistent
    /// container — using the condition CREATES the platform's manager, which is right:
    /// the configuration wants the feature — and a platform-aware matcher takes effect
    /// wherever one claimed the default.
    /// </summary>
    public abstract class NetworkChangeCondition(InterfaceMatcher matcher) : IEnvironmentCondition
    {
        readonly Lock _lock = new();

        EventHandler? _changed;

        public required INetworkInterfaceManager Manager { private get; init; }

        public bool IsSatisfied() => Manager.Any(matcher.Matches);

        public event EventHandler? Changed
        {
            add
            {
                lock (_lock)
                {
                    bool first = _changed is null;

                    _changed += value;

                    if (first && _changed is not null)
                    {
                        Manager.Changed += OnManagerChanged;
                    }
                }
            }
            remove
            {
                lock (_lock)
                {
                    bool subscribed = _changed is not null;

                    _changed -= value;

                    if (subscribed && _changed is null)
                    {
                        Manager.Changed -= OnManagerChanged;
                    }
                }
            }
        }

        private void OnManagerChanged(object? sender, EventArgs e) => _changed?.Invoke(this, EventArgs.Empty);
    }
}
