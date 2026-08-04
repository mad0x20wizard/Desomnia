using System.Diagnostics;

namespace MadWizard.Desomnia.Processes.Manager
{
    /**
     * The base of every process decorator: forwards the whole of IProcess to the process behind
     * it, so a metric can wrap what it measures around a platform's process without the platform
     * knowing. Decorators are applied at resolution – a service middleware wraps the activated
     * process – so they exist exactly when whatever needs them is configured, and they stack.
     *
     * The roster holds the outermost layer. Whoever needs the platform's own object underneath –
     * a cast to ProcessHandle, a native handle – unwraps explicitly rather than assuming the
     * roster hands out concrete types.
     */
    public abstract class ProcessDecorator(IProcess process) : IProcess
    {
        private IProcess Target => process;

        /// <summary>
        /// The first layer of the given type. Decorators stack, so whoever needs a specific
        /// layer – a metric its own decoration, a platform its concrete process – walks the
        /// layers instead of type-testing the outermost. Throws where the cast it replaces
        /// would have: asking for a layer that is not there is a wiring error, not a condition.
        /// </summary>
        public T Layer<T>() where T : IProcess
        {
            var layer = process;

            while (true)
            {
                if (layer is T match)
                    return match;

                if (layer is not ProcessDecorator decorator)
                    throw new InvalidCastException($"'{process}' carries no {typeof(T).Name} layer");

                layer = decorator.Target;
            }
        }

        public virtual int Id => process.Id;
        public virtual int SessionId => process.SessionId;
        public virtual string Name => process.Name;
        public virtual string? ImagePath => process.ImagePath;

        public virtual TimeSpan? ProcessorTime => process.ProcessorTime;
        public virtual ProcessInputOutput? StorageData => process.StorageData;
        public virtual ProcessInputOutput? NetworkData => process.NetworkData;

        public virtual IProcess? Parent => process.Parent;

        public virtual bool HasStopped => process.HasStopped;

        public virtual Process Native => process.Native;

        public virtual Task Stop(TimeSpan timeout = default) => process.Stop(timeout);

        public event EventHandler? Stopped
        {
            add => process.Stopped += value;
            remove => process.Stopped -= value;
        }

        public virtual void Dispose() => process.Dispose();
    }

    public static class ProcessDecoratorExt
    {
        extension(IProcess process)
        {
            public T Layer<T>() where T : IProcess
            {
                if (process is ProcessDecorator decorator)
                {
                    return decorator.Layer<T>();
                }

                throw new InvalidCastException($"'{process}' carries no {typeof(T).Name} layer");
            }
        }
    }

}
