using System;
using System.Threading;
using System.Diagnostics;

using MadWizard.Desomnia.Pipe.Config;
using System.Threading.Tasks;
using MadWizard.Desomnia.Pipe.Messages;
using System.Timers;

using Timer = System.Timers.Timer;
using MadWizard.Desomnia.Pipe;

namespace MadWizard.Desomnia.Minion
{
    class MinionStartup : IDisposable
    {
        const string CMD_AWAIT_DEBUGGER = "/WaitForDebugger";

        ManualResetEvent _waitUntilFinished;

        public MinionStartup(string[] args)
        {
            foreach (string arg in args)
            {
                if (arg.StartsWith(CMD_AWAIT_DEBUGGER))
                {
                    //while (!Debugger.IsAttached)
                    //    Thread.Sleep(100);
                }
            }

            PipeClient = new MessagePipeClient((uint)Process.GetCurrentProcess().SessionId);
        }

        public MessagePipeClient PipeClient { get; private set; }

        public async Task<MinionConfig> ConnectToService(int timeout = 5000)
        {
            var source = new TaskCompletionSource<MinionConfig>(TaskCreationOptions.RunContinuationsAsynchronously);

            var timer = new Timer(timeout);
            timer.Elapsed += Timer_Elapsed;
            timer.AutoReset = false;
            timer.Start();

            #region PipeClient/Timer Callbacks
            void PipeClient_ReceivedMessage(object sender, Message message)
            {
                if (message is StartupMessage startupMessage)
                {
                    source.SetResult(startupMessage.Config);

                    /*
                    * Wir halten die NamedPipe-Verarbeitung solange an, bis wir das Signal kriegen,
                    * dass der Boot-Prozess abgeschlossen ist.
                    */
                    (_waitUntilFinished = new ManualResetEvent(false)).WaitOne();

                    PipeClient.SendMessage(new ReadyMessage());
                }
                else if (message is TerminateMessage)
                {
                    source.SetException(new Exception("Terminated"));
                }
                else
                {
                    source.SetException(new Exception($"Unexpected Message = {message.GetType().Name}"));
                }
            }
            void PipeClient_Disconnected(object sender, EventArgs e)
            {
                source.SetException(new Exception("Disconected"));
            }
            void PipeClient_Error(object sender, Exception error)
            {
                source.SetException(new Exception("Error"));
            }
            void Timer_Elapsed(object sender, ElapsedEventArgs e)
            {
                source.SetException(new TimeoutException());
            }
            #endregion

            try
            {
                PipeClient.MessageReceived += PipeClient_ReceivedMessage;
                PipeClient.Disconnected += PipeClient_Disconnected;
                PipeClient.Error += PipeClient_Error;

                await PipeClient.Connect(new CancellationTokenSource(timeout).Token);

                return await source.Task;
            }
            catch (Exception)
            {
                try { PipeClient.Dispose(); } catch { }

                throw;
            }
            finally
            {
                PipeClient.Error -= PipeClient_Error;
                PipeClient.Disconnected -= PipeClient_Disconnected;
                PipeClient.MessageReceived -= PipeClient_ReceivedMessage;
            }
        }

        void IDisposable.Dispose()
        {
            if (_waitUntilFinished != null)
            {
                _waitUntilFinished.Set();
                _waitUntilFinished.Dispose();
            }
        }
    }
}