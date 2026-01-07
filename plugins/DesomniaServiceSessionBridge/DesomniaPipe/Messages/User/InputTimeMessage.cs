using MessagePack;
using System;
using System.Collections.Generic;
using System.Text;

namespace MadWizard.Desomnia.Pipe.Messages
{
    public class InputTimeMessage : UserMessage
    {
        public InputTimeMessage(DateTime lastInputTime)
        {
            LastInputTime = lastInputTime;
        }

        public DateTime LastInputTime { get; set; }
    }
}
