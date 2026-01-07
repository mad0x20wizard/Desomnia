using System;
using System.Collections.Generic;
using System.Text;

namespace MadWizard.Desomnia.Pipe.Messages
{
    public class ConnectToConsoleMessage : UserMessage
    {
        public ConnectToConsoleMessage(uint? sessionID)
        {
            SessionID = sessionID;
        }

        public uint? SessionID { get; set; }
    }
}
