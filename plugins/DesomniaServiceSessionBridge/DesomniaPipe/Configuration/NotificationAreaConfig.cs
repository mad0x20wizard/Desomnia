using MadWizard.Desomnia.Pipe.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.Pipe.Config
{
    public struct NotificationAreaConfig
    {
        public IDictionary<uint, SessionInfo> Sessions { get; set; }

        public bool Sleepless => SleeplessUntil != null;
        public bool? SleeplessIfUsage { get; set; }
        public DateTime? SleeplessUntil { get; set; }

        public bool MayConfigureSleepless { get; set; }
        public bool MaySuspendSystem { get; set; }
    }

    public struct SessionInfo
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public string ClientName { get; set; }

        public bool IsConsoleConnected { get; set; }
        public bool IsRemoteConnected { get; set; }

        public bool MayConnectToConsole { get; set; }
        public bool MayDisconnect { get; set; }
    }

}
