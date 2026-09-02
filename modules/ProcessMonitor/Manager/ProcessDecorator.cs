using Autofac.Features.Decorators;
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
    public abstract class ProcessDecorator : IProcess
    {
        internal IProcess Target { get; init; }

        public ProcessDecorator(IProcess process, IDecoratorContext context)
        {
            Target = process;
        }

        public virtual int Id => Target.Id;
        public virtual int SessionId => Target.SessionId;
        public virtual string Name => Target.Name;
        public virtual string? ImagePath => Target.ImagePath;

        public virtual TimeSpan? ProcessorTime => Target.ProcessorTime;
        public virtual TimeSpan? GraphicsProcessorTime => Target.GraphicsProcessorTime;
        public virtual ProcessInputOutput? StorageData => Target.StorageData;
        public virtual ProcessInputOutput? NetworkData => Target.NetworkData;

        public virtual IProcess? Parent => Target.Parent;

        public virtual bool HasStopped => Target.HasStopped;

        public virtual Process Native => Target.Native;

        public virtual Task Stop(TimeSpan timeout = default) => Target.Stop(timeout);

        public event EventHandler? Stopped
        {
            add => Target.Stopped += value;
            remove => Target.Stopped -= value;
        }

        public virtual void Dispose() => Target.Dispose();

        public override string? ToString()
        {
            return Target.ToString();
        }
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
