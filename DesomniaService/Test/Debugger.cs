using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.Helper
{
    internal static class Debugger
    {
        public static async Task UntilAttached()
        {
            Console.WriteLine("Waiting for debugger to attach");

            while (!System.Diagnostics.Debugger.IsAttached)
                await Task.Delay(100);

            Console.WriteLine("Debugger attached");
        }
    }
}
