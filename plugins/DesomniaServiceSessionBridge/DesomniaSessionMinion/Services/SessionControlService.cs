using Autofac;
using MadWizard.Desomnia.Pipe;
using MadWizard.Desomnia.Pipe.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.Minion
{
    public class SessionControlService
    {
        public SessionControlService(PipeMessageBroker broker)
        {
            broker.RegisterMessageHandler<LockMessage>(HandleLockMessage);
        }

        private void HandleLockMessage(LockMessage message)
        {
            LockWorkStation();
        }

        [DllImport("user32.dll")]
        private static extern bool LockWorkStation();
    }
}
