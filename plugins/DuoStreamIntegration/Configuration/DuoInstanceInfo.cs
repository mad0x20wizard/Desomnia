using MadWizard.Desomnia.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.Service.Duo.Configuration
{
    public class DuoInstanceInfo(string name)
    {
        public string Name { get; set; } = name;

        public NamedAction? OnDemand { get; set; }

        public DelayedAction? OnIdle { get; set; }

        public ScheduledAction? OnLogin { get; set; }
        public ScheduledAction? OnStart { get; set; }
        public ScheduledAction? OnStop { get; set; }
        public ScheduledAction? OnLogoff { get; set; }

    }
}
