using MadWizard.Desomnia.Process.Manager;

namespace MadWizard.Desomnia.Session.Manager
{
    public interface ISession : IProcessManager
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

        public Task Disconnect();
        public Task Logoff();
        public Task Lock();

        public event EventHandler Locked;
        public event EventHandler Unlocked;
        public event EventHandler Connected;
        public event EventHandler Disconnected;

    }
}
