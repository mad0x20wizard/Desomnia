namespace MadWizard.Desomnia.Session.Manager
{
    public interface ISessionManager : IIEnumerable<ISession>
    {
        ISession this[uint sid] { get; }

        ISession? ConsoleSession { get; set; }

        IEnumerable<ISession> FindSessionsByUserName(string user);

        event EventHandler<ISession> UserLogon;
        event EventHandler<ISession> RemoteConnect;
        event EventHandler<ISession> ConsoleConnect;
        event EventHandler<ISession> RemoteDisconnect;
        event EventHandler<ISession> ConsoleDisconnect;
        event EventHandler<ISession> UserLogoff;

    }
}
