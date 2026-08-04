using System.ComponentModel;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;

namespace MadWizard.Desomnia.Processes.Manager
{
    /**
     * The BCL-backed IProcess: whatever the platform did not answer for itself is answered by a
     * System.Diagnostics.Process.
     *
     * That object is created on first demand and, for a process nobody asks anything of, never at
     * all – which is the point of asking nothing here that the description already carries. A single
     * question to it makes the runtime describe every process on the machine, on Unix walking every
     * thread of every one of them. A platform that can do better overrides the members it has a
     * cheaper answer for, and every one of them is written to be answerable lazily.
     */
    public class ProcessHandle(ProcessInformation info, IProcess? parent = null) : IProcess
    {
        protected readonly CompositeDisposable _heldResources = [];

        /// <summary>
        /// The BCL process object, created once and kept – callers write state into this very
        /// instance (redirected output handles) and read it back through a later access.
        /// </summary>
        public Process Native
        {
            get
            {   try
                {
                    lock (_heldResources)
                    {
                        return field ??= Process.GetProcessById(info.Id).DisposeWith(_heldResources);
                    }
                }
                catch (ArgumentException)
                {
                    throw new ProcessNotFoundException(info.Id);
                }
            }
        } = info.Native;

        public IProcess? Parent => parent;

        public int Id => info.Id;
        public int SessionId => info.SessionId ?? Native.SessionId;
        public string Name => info.Name ?? Native.ProcessName;

        public virtual string? ImagePath
        {
            protected init;

            get
            {
                if (field == null)
                {
                    try
                    {
                        if (Native.MainModule is ProcessModule module)
                        {
                            field = module.FileName;
                        }
                    }
                    catch (Exception)
                    {
                        // some processes don't like to be asked this...
                    }
                }

                return field;
            }
        } = info.ImagePath;

        public virtual TimeSpan? ProcessorTime
        {
            get
            {
                try
                {
                    return Native.TotalProcessorTime;
                }
                catch (SystemException ex) when (ex is ProcessNotFoundException or InvalidOperationException or Win32Exception)
                {
                    return null; // gone, or never ours to ask
                }
            }
        }

        /**
         * The two counter pairs have no BCL fallback to reach for – System.Diagnostics.Process
         * simply has nothing to offer – so the base answers "cannot sample" and only a platform
         * that actually measured something overrides. That keeps an un-ported platform failing
         * visibly (the watch warns and stops trusting the threshold) instead of failing wrong.
         */
        public virtual ProcessInputOutput? StorageData => null;
        public virtual ProcessInputOutput? NetworkData => null;

        public virtual bool HasStopped
        {
            get
            {
                try
                {
                    return Native.HasExited;
                }
                catch (ProcessNotFoundException)
                {
                    return true; // the pid is gone, so there is nothing left to materialize
                }
            }
        }

        /**
         * Asks the process to stop, and reports whether there was anything to ask.
         *
         * On Windows the request is a close message to the process' main window, which a process
         * without one never receives – hence the answer, rather than a wait for something that was
         * never going to happen. Platforms with a signal for this override it.
         */
        protected virtual bool RequestStop() => Native.CloseMainWindow();

        /**
         * Stops the process, letting it stop itself where there is time for that.
         *
         * The timeout is what buys that chance. Without one this is a termination and nothing else,
         * which is exactly what the configured action means by a stop with no timeout set: asking
         * politely and then killing in the same breath would only pretend to be graceful.
         */
        public virtual async Task Stop(TimeSpan timeout = default)
        {
            try
            {
                if (HasStopped)
                    return;

                if (timeout > TimeSpan.Zero && RequestStop())
                {
                    using var grace = new CancellationTokenSource(timeout);

                    try
                    {
                        await Native.WaitForExitAsync(grace.Token);
                    }
                    catch (OperationCanceledException) 
                    {
                        // timeout, let's kill
                    }
                }

                if (!HasStopped)
                {
                    Native.Kill();
                }
            }
            catch (ProcessNotFoundException)
            {
                // there is nothing to stop (anymore)
            }
        }

        public event EventHandler? Stopped;

        /**
         * Reports that this process is gone – fires once, however often it is called.
         *
         * The subscribers are dropped *before* they are invoked, and deliberately so: the manager
         * is one of them, and it answers by removing the process, which comes straight back here.
         * Clearing first makes that second visit a no-op. Invoking first would still terminate –
         * the manager's own removal is what stops it – but every subscriber would hear about the
         * same exit twice.
         */
        internal protected virtual void TriggerStop()
        {
            var stopped = Stopped;

            Stopped = null;

            stopped?.Invoke(this, EventArgs.Empty);
        }

        public virtual void Dispose()
        {
            lock (_heldResources)
            {
                _heldResources.Dispose();
            }
        }
    }
}
