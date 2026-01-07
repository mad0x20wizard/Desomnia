using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia
{
    public readonly struct EventOptions
    {
        public EventOptions()
        {

        }

        public bool Bubbles { get; init; } = false;
    }
}
