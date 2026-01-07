using Autofac.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.Service.Duo
{
    internal class DuoStreamUsage(string name) : UsageToken
    {
        public string Name => name;

        public override string ToString() => $"DuoStream<{name}>";
    }
}
