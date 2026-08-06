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
        internal IProcess Target => process;

        public virtual int Id => process.Id;
        public virtual int SessionId => process.SessionId;
        public virtual string Name => process.Name;
        public virtual string? ImagePath => process.ImagePath;

        public virtual TimeSpan? ProcessorTime => process.ProcessorTime;
        public virtual TimeSpan? GraphicsProcessorTime => process.GraphicsProcessorTime;
        public virtual object GraphicsProcessorScope => process.GraphicsProcessorScope;
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
                do
                {
                    switch (process)
                    {
                        case T target:
                            return target;

                        case ProcessDecorator decorator when decorator is T targetDecorator:
                            return targetDecorator;

                        case ProcessDecorator unwrap:
                            process = unwrap.Target;
                            continue;

                        default:
                            throw new InvalidCastException($"'{process}' carries no {typeof(T).Name} layer");
                    }
                }
                while (true);
            }
        }
    }
}
