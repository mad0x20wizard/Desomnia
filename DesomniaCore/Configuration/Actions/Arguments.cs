using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.Configuration
{
    public class Arguments
    {
        private object[] _args;

        public Arguments(object[] args)
        { 
            _args = args;
        }

        public object this[int index] => _args[index];

        public int Length => _args.Length;

        public static Arguments Merge(Arguments? args, object[] defaultArgs)
        {
            if (args == null)
            {
                return new Arguments(defaultArgs);
            }
            else if (args.Length > defaultArgs.Length)
            {
                return args;
            }
            else
            {
                for (int i = 0; i < args.Length; i++)
                {
                    defaultArgs[i] = args[i];
                }

                return new Arguments(defaultArgs);
            }
        }

        public override string ToString()
        {
            return $"({string.Join(", ", _args.Select(arg => arg is string ? $"'{arg}'" : arg.ToString()))})";
        }
    }
}
