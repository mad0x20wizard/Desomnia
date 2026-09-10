using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Service.Duo.Manager
{
    public abstract partial class DuoManager
    {
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            SubscribeSessionEvents();

            try
            {
                await base.StartAsync(cancellationToken);
            }
            catch
            {
                UnsubscribeSessionEvents();
                throw;
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            UnsubscribeSessionEvents();
            await base.StopAsync(cancellationToken);
        }

        private void SubscribeSessionEvents()
        {
            lock (_sessionSubscriptionMutex)
            {
                if (_sessionsSubscribed || Volatile.Read(ref _disposed) != 0)
                    return;

                SessionManager.UserLogon += SessionManager_UserLogon;
                SessionManager.UserLogoff += SessionManager_UserLogoff;
                _sessionsSubscribed = true;
            }
        }

        private void UnsubscribeSessionEvents()
        {
            lock (_sessionSubscriptionMutex)
            {
                if (!_sessionsSubscribed)
                    return;

                _sessionsSubscribed = false;
                SessionManager.UserLogon -= SessionManager_UserLogon;
                SessionManager.UserLogoff -= SessionManager_UserLogoff;
            }
        }

        private void SessionManager_UserLogon(object? sender, ISession session)
        {
            var generation = Volatile.Read(ref _generation);
            var instance = generation?.Instances.FirstOrDefault(candidate => candidate.HasInitiated(session));

            if (instance != null && Owns(generation, instance))
                instance.Session = session;
        }

        private void SessionManager_UserLogoff(object? sender, ISession session)
        {
            var generation = Volatile.Read(ref _generation);
            var instance = generation?.Instances.FirstOrDefault(candidate => candidate.Session == session);

            if (instance != null && Owns(generation, instance))
                instance.Session = null;
        }

        public override void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            UnsubscribeSessionEvents();

            _ = Volatile.Read(ref _generation)?.CancelAsync();

            var worker = ExecuteTask;
            base.Dispose();
            _ = FinishDisposal(worker);
        }

        private async Task FinishDisposal(Task? worker)
        {
            try
            {
                if (worker != null)
                    await worker;
            }
            catch (OperationCanceledException)
            {
                // Normal synchronous container teardown.
            }
            catch (Exception ex)
            {
                DuoGeneration.LogCleanupError(Logger, ex, "Duo manager worker failed while shutting down");
            }

            try
            {
                await TriggerStopped(notify: false);
            }
            catch (Exception ex)
            {
                DuoGeneration.LogCleanupError(Logger, ex, "Could not finish Duo manager cleanup");
            }
            finally
            {
                if (_service != null)
                    DuoGeneration.DisposeResources(Logger, [_service]);
            }
        }
    }
}
