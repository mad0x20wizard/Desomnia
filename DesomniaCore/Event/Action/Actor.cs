using MadWizard.Desomnia.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace MadWizard.Desomnia
{
    public abstract class Actor : EventSource, IDisposable
    {
        private readonly Dictionary<string, ActionHandler> _actionHandlers = [];

        private readonly Dictionary<string, DelayedInvocation> _actionInvocations = [];

        protected Actor()
        {
            foreach (var methodInfo in GetType().GetAllMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(m => m.GetCustomAttribute<ActionHandlerAttribute>() != null))
            {
                var handler = new ActionHandler(methodInfo);

                _actionHandlers.Add(handler.Name, handler);
            }
        }

        public void AddEventAction(string eventName, NamedAction? action)
        {
            if (action == null || action.Name.Trim() == string.Empty)
                return;

            async Task invocation(Event eventRef)
            {
                try
                {
                    if (!await HandleEventAction(eventRef, action))
                    {
                        throw new NotImplementedException($"action '{action.Name}' not found on {GetType().Name} for event {eventRef}");
                    }
                }
                catch (Exception ex)
                {
                    if (!HandleActionError(new ActionError(eventRef, action, ex)))
                    {
                        throw;
                    }
                }
            }

            if (action is ScheduledAction scheduledAction && scheduledAction.Delay != TimeSpan.Zero)
            {
                async Task delayedInvocation(Event eventRef)
                {
                    if (!_actionInvocations.ContainsKey(eventName))
                    {
                        _actionInvocations[eventName] = new ScheduledInvocation(scheduledAction.Delay);

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _actionInvocations[eventName].WaitTask;

                                await invocation(eventRef);
                            }
                            catch (TaskCanceledException)
                            {
                                // ignore
                            }
                            finally
                            {
                                _actionInvocations.Remove(eventName);
                            }
                        });
                    }
                }

                AddEventHandler(eventName, delayedInvocation);
            }

            else if (action is ThrottledAction throttledAction && throttledAction.Times > 0)
            {
                async Task throttledInvocation(Event eventRef)
                {
                    _actionInvocations.TryGetValue(eventName, out DelayedInvocation? delayed);

                    if (delayed is ThrottledInvocation throttled)
                        throttled.Trigger();

                    else
                    {
                        _actionInvocations[eventName] = delayed = new ThrottledInvocation(throttledAction.Times);

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await delayed.WaitTask;

                                await invocation(eventRef);
                            }
                            catch (OperationCanceledException)
                            {
                                // ignore
                            }
                            finally
                            {
                                _actionInvocations.Remove(eventName);
                            }
                        });
                    }
                }

                AddEventHandler(eventName, throttledInvocation);
            }

            else
            {
                AddEventHandler(eventName, invocation);
            }
        }

        internal Task<bool> TryHandleEventAction(Event eventRef, NamedAction action) => HandleEventAction(eventRef, action);

        protected virtual async Task<bool> HandleEventAction(Event @event, NamedAction action)
        {
            if (_actionHandlers.TryGetValue(action.Name, out var handler))
            {
                if (handler.ShouldSkipInvocation(@event))
                    return true;

                if (handler.PrepareWithContext(this, action.Arguments, [.. @event.Context]) is ActionInvocation invocation) // TODO: is this correct?
                {
                    try
                    {
                        LogEventInvocation(@event, action);

                        handler.Invocations.Add(invocation);

                        await invocation.InvokeAsync();
                    }
                    catch (Exception ex)
                    {
                        if (!HandleActionError(new ActionError(@event, action, ex) { Actor = this }))
                        {
                            throw;
                        }
                    }
                    finally
                    {
                        handler.Invocations.TryRemove(invocation);
                    }
                }

                return true;
            }

            return false;
        }

        protected virtual bool HandleActionError(ActionError error) => false;

        private void LogEventInvocation(Event eventRef, NamedAction action)
        {
            var logger = (ILogger?)GetType().GetAllFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(f => typeof(ILogger).IsAssignableFrom(f.FieldType)).FirstOrDefault()?.GetValue(this);

            logger?.LogDebug($"{eventRef} -> {action}" + (eventRef.Source != this ? $" @ {this.GetType().Name}" : ""));
        }

        protected void CancelEventAction(string eventName)
        {
            if (_actionInvocations.Remove(eventName, out var invocation))
            {
                invocation?.Cancel();
            }
        }

        public virtual void Dispose()
        {
            foreach (var invocation in _actionInvocations.Values)
                invocation.Cancel();

            _actionInvocations.Clear();
        }

        private abstract class DelayedInvocation
        {
            protected CancellationTokenSource Source { get; } = new CancellationTokenSource();

            public abstract Task WaitTask { get; }

            public void Cancel()
            {
                Source.Cancel();
            }
        }

        private class ScheduledInvocation(TimeSpan delay) : DelayedInvocation
        {
            public override Task WaitTask => Task.Delay(delay, Source.Token);
        }

        private class ThrottledInvocation(uint times) : DelayedInvocation
        {
            private uint _timesLeft = times;

            private SemaphoreSlim _semaphore = new(0);

            public override Task WaitTask => _semaphore.WaitAsync(Source.Token);

            public void Trigger()
            {
                if (Interlocked.Decrement(ref _timesLeft) == 0)
                {
                    _semaphore.Release();
                }
            }
        }
    }
}
