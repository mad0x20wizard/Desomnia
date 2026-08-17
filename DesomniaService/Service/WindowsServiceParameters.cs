using System.CommandLine;

namespace MadWizard.Desomnia.Application
{
    internal class WindowsServiceParameters : RootCommand
    {
        static internal readonly Option<bool> DebugOption = new("--debug", "/debug")
        {
            Description = "Wait for a debugger to attach before starting.",
        };

        internal WindowsServiceParameters() : base("Desomnia Sleep Management")
        {
            Add(DebugOption);

            SetAction(Run);
        }

        private void Run(ParseResult result)
        {
            if (result.GetValue(DebugOption))
            {
                Test.Debugger.UntilAttached().Wait();
            }
        }
    }
}
