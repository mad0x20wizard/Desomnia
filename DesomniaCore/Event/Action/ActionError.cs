using MadWizard.Desomnia.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia
{
    public class ActionError(Event originalEvent, NamedAction action, Exception exception)
    {
        public Event Event => originalEvent;
        public NamedAction Action => action;
        public Exception? Exception => exception is TargetInvocationException target ? target.InnerException : exception;

        public Actor? Actor { get; init; }
    }
}
