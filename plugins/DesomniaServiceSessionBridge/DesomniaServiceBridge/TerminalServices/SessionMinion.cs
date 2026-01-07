using MadWizard.Desomnia.Pipe;
using MadWizard.Desomnia.Pipe.Config;
using MadWizard.Desomnia.Pipe.Messages;
using MadWizard.Desomnia.Session.Manager.Bridged;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;

using Timer = System.Timers.Timer;

namespace MadWizard.Desomnia.Service.Bridge.Minion
{
    internal class SessionMinion : IDisposable
    {
        private static readonly bool RUN_AS_SYSTEM = false;  // don't do this

        private MinionConfig _config;

        private BridgedTerminalServicesSession _session;

        private System.Diagnostics.Process _process;

        private MessagePipeServer _pipe;

        private SessionMinionStatus _status = SessionMinionStatus.None;

        private ConcurrentQueue<UserMessage> _queueUntilReady = [];

        internal SessionMinion(MinionConfig config, BridgedTerminalServicesSession session)
        {
            _config = config;

            _session = session;

            if (RUN_AS_SYSTEM)
            {
                _pipe = new(session.Id);
            }
            else
            {
                var security = new PipeSecurity();
                security.AddAccessRule(new(
                    new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), 
                    PipeAccessRights.FullControl,
                    AccessControlType.Allow));

                var stream = NamedPipeServerStreamAcl.Create(MessagePipe.PipeNameBySessionId(session.Id),
                    PipeDirection.InOut, 1,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous |
                    PipeOptions.WriteThrough, 0, 0,
                    security);

                _pipe = new(stream);
            }

            _pipe.Connected += Pipe_Connected;
            _pipe.MessageReceived += Pipe_MessageReceived;
            _pipe.Disconnected += Pipe_Disconnected;
            _pipe.Start(); // TODO timeout!

            _process = LaunchProcess();
            _process.Exited += Process_Exited;

            _status = SessionMinionStatus.Launched;

            Ready += SessionMinion_Ready;
        }

        public uint SessionID => _session.Id;

        event EventHandler? Ready;
        public event EventHandler<UserMessage>? MessageReceived;
        public event EventHandler? Terminated;

        private System.Diagnostics.Process LaunchProcess()
        {
            var dir = new FileInfo(Assembly.GetExecutingAssembly().Locati‌​on).Directory!;

            var startup = new ProcessStartInfo()
            {
                FileName = Path.Combine(dir.FullName, "minion", "DesomniaSessionMinion.exe"),
                WorkingDirectory = Directory.GetCurrentDirectory()
            };


            //#if DEBUG
            if (!Debugger.IsAttached) startup.ArgumentList.Add("/WaitForDebugger");
            //#endif

            return _session.LaunchProcess(startup, RUN_AS_SYSTEM);
        }

        private void SessionMinion_Ready(object? sender, EventArgs e)
        {
            while (_queueUntilReady.TryDequeue(out var message))
            {
                _pipe.SendMessage(message);
            }
        }

        private void Pipe_Connected(object? sender, EventArgs e)
        {
            if (_pipe.ClientProcessId == _process.Id) // only accept messages from our own process
            {
                _status = SessionMinionStatus.Startup;

                _pipe.SendMessage(new StartupMessage(_config));
            }
            else
            {
                _pipe.Disconnect(); // TODO maybe implement recover/resilience
            }
        }

        private void Pipe_MessageReceived(object? sender, Message message)
        {
            switch (message)
            {
                case UserMessage user:
                    MessageReceived?.Invoke(this, user);
                    break;

                case ReadyMessage ready:
                    _status = SessionMinionStatus.Ready;
                    Ready?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        private void Pipe_Disconnected(object? sender, EventArgs e)
        {
            if (!_process.HasExited)
            {
                _process.Kill();
            }
        }

        private void Process_Exited(object? sender, EventArgs e)
        {
            _status = SessionMinionStatus.Stopped;

            Terminated?.Invoke(this, EventArgs.Empty);
        }

        public Task UntilReady()
        {
            if (_status == SessionMinionStatus.Ready)
                return Task.CompletedTask;

            var source = new TaskCompletionSource<bool>();

            Ready += (sender, e) => source.SetResult(true);

            return source.Task;
        }

        internal Task Terminate(TimeSpan? timeout = null)
        {
            if (_process.HasExited)
                return Task.CompletedTask;

            _status = SessionMinionStatus.Terminating;

            var taskSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (timeout != null)
            {
                var timer = new Timer(timeout.Value) { AutoReset = false };

                timer.Elapsed += (timer, e) =>
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill();
                    }
                };

                timer.Start();
            }

            Terminated += (s, e) => taskSource.SetResult(true);

            _pipe.SendMessage(new TerminateMessage());

            return taskSource.Task;
        }

        public void Send(UserMessage message)
        {
            if (_status != SessionMinionStatus.Ready)
                _queueUntilReady.Enqueue(message);
            else
                _pipe.SendMessage(message);
        }

        public void Dispose()
        {
            Terminate(TimeSpan.FromSeconds(5));//.Wait();

            _pipe.Dispose();
        }
    }

    public enum SessionMinionStatus
    {
        None = 0,

        Launched,
        Startup,
        Ready,
        Terminating,
        Stopped
    }
}
