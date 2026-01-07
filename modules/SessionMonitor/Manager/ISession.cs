using MadWizard.Desomnia.Process.Manager;
using System.Diagnostics;

namespace MadWizard.Desomnia.Session.Manager
{
    public interface ISession
    {
        public uint Id { get; }

        public string UserName { get; }
        public string? ClientName { get; }

        public bool IsConnected { get; }
        public bool IsConsoleConnected { get; }
        public bool IsRemoteConnected { get; }
        public bool IsAdministrator { get; }
        public bool IsUser { get; }

        public bool? IsLocked { get; }

        public TimeSpan? IdleTime => LastInputTime != null ? DateTime.Now - LastInputTime : null;
        public DateTime? LastInputTime => IdleTime != null ? DateTime.Now - IdleTime : null;

        public IEnumerable<IProcess> Processes { get; }
        public IProcess LaunchProcess(ProcessStartInfo info);

        public Task Disconnect();
        public Task Logoff();
        public Task Lock();

        public event EventHandler Locked;
        public event EventHandler Unlocked;
        public event EventHandler Connected;
        public event EventHandler Disconnected;

    }
}
