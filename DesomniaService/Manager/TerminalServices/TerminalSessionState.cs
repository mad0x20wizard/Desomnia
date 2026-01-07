using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.Session.Manager
{
    public enum TerminalSessionState
    {
        //
        // Zusammenfassung:
        //     A user is logged on to the session.
        Active,
        //
        // Zusammenfassung:
        //     A client is connected to the session.
        Connected,
        //
        // Zusammenfassung:
        //     The session is in the process of connecting to a client.
        ConnectQuery,
        //
        // Zusammenfassung:
        //     This session is shadowing another session.
        Shadow,
        //
        // Zusammenfassung:
        //     The session is active, but the client has disconnected from it.
        Disconnected,
        //
        // Zusammenfassung:
        //     The session is waiting for a client to connect.
        Idle,
        //
        // Zusammenfassung:
        //     The session is listening for connections.
        Listen,
        //
        // Zusammenfassung:
        //     The session is being reset.
        Reset,
        //
        // Zusammenfassung:
        //     The session is down due to an error.
        Down,
        //
        // Zusammenfassung:
        //     The session is initializing.
        Init
    }
}
