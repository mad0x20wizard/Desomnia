using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.Network.HyperV.Events
{
    internal enum HyperVEventID
    {
        VM_STARTED = 18601,
        VM_SUSPENDED = 18510,
        VM_RESTORED = 18596,
        VM_SHUTDOWN = 18504,
        VM_STOPPED = 18502,
    }
}
