using Autofac;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Neighborhood;
using Microsoft.Extensions.Logging;
using System.Reactive.Disposables;

namespace MadWizard.Desomnia.Network.Logging
{
    internal class NetworkLogger<T>(ILogger<T> logger, ILifetimeScope scope) : ILogger<T>
    {
        public required NetworkContext Network { private get; set; }

        private NetworkHost? Host => scope.ResolveOptional<NetworkHost>();

        private IDisposable? MaybeBeginNetworkScope()
        {
            if (!NLog.ScopeContext.TryGetProperty("Network", out _))
            {
                return logger.BeginScope(new Dictionary<string, object> { ["Network"] = Network });
            }

            return null;
        }

        IDisposable? ILogger.BeginScope<TState>(TState state)
        {
            var scope = logger.BeginScope(state);

            try
            {
                if (scope != null && MaybeBeginNetworkScope() is IDisposable network)
                {
                    return new CompositeDisposable(network, scope);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not start network scope");
            }

            return scope;
        }

        void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            try
            {
                using var network = MaybeBeginNetworkScope();

                using var host = Host is not null ? logger.BeginHostScope(Host) : null;

                logger.Log(logLevel, eventId, state, exception, formatter);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not resolves context");

                logger.Log(logLevel, eventId, state, exception, formatter);
            }
        }

        bool ILogger.IsEnabled(LogLevel logLevel)
        {
            return logger.IsEnabled(logLevel);
        }
    }
}
