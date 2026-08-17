using System.CommandLine;

namespace MadWizard.Desomnia.Application
{
    internal class ApplicationCommandLine : RootCommand
    {
        static internal readonly Option<bool> AutoReloadOption = new("--auto-reload", "-a")
        {
            Description = "Enable automatic reloading after config file changed."
        };
        static internal readonly Option<bool> DebugOption = new("--debug")
        {
            Description = "Wait for a debugger to attach before starting.",
        };

        internal ApplicationCommandLine() : base("Desomnia Sleep Management")
        {
            Add(AutoReloadOption);
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
