using MadWizard.Desomnia.Pipe.Config;
using System;
using System.Collections.Generic;
using System.Text;

namespace MadWizard.Desomnia.Pipe.Messages
{
    public class RequestInspectionMessage : UserMessage
    {

    }

    public class InspectionMessage : UserMessage
    {
        public IList<UsageTokenInfo> Tokens { get; set; }

        public DateTime Time { get; set; }
        public DateTime? NextTime { get; set; }
    }

    public struct UsageTokenInfo
    {
        public string DisplayName { get; set; }
        public string TypeName { get; set; }

        public IList<UsageTokenInfo> Tokens { get; set; }
    }

    public struct IconInfo
    {
        public string Name { get; set; }
        public string Path { get; set; }
    }

}
