using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.Session.Manager
{
    public interface ISessionManager : IIEnumerable<ISession>
    {
        ISession this[uint sid] { get; }

        ISession? ConsoleSession { get; set; }

        ISession? FindSessionByID(uint sid);
        ISession? FindSessionByUserName(string user);

        event EventHandler<ISession> UserLogon;
        event EventHandler<ISession> RemoteConnect;
        event EventHandler<ISession> ConsoleConnect;
        event EventHandler<ISession> RemoteDisconnect;
        event EventHandler<ISession> ConsoleDisconnect;
        event EventHandler<ISession> UserLogoff;

    }
}
