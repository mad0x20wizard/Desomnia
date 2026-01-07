using Autofac;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Logging
{
    internal class AsyncExceptionLogger : IStartable, IDisposable
    {
        public required ILogger<AsyncExceptionLogger> Logger { private get; init; }

        void IStartable.Start()
        {
            TaskScheduler.UnobservedTaskException += LogUnobservedException;
        }

        private void LogUnobservedException(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            Logger.LogError(args.Exception, "Unobserved Task Exception");

            args.SetObserved(); // Prevents app crash
        }

        void IDisposable.Dispose()
        {
            TaskScheduler.UnobservedTaskException -= LogUnobservedException;
        }
    }
}
