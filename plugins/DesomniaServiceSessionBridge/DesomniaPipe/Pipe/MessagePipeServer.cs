using MadWizard.Desomnia.Pipe.Messages;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MadWizard.Desomnia.Pipe
{
    public class MessagePipeServer : MessagePipe<NamedPipeServerStream>
    {
        public MessagePipeServer(NamedPipeServerStream stream) : base(stream)
        {

        }

        public MessagePipeServer(uint sid) 
            : this(new NamedPipeServerStream(PipeNameBySessionId(sid),
                PipeDirection.InOut, 1,
                PipeTransmissionMode.Message,
                PipeOptions.Asynchronous |
                PipeOptions.WriteThrough))
        {
            
        }

        public uint ClientProcessId
        {
            get
            {
                if (GetNamedPipeClientProcessId(_stream.SafePipeHandle, out uint clientProcessId))
                    return clientProcessId;

                throw new Win32Exception();
            }
        }

        public async void Start(CancellationToken? cancellation = null)
        {
            try
            {
                await _stream.WaitForConnectionAsync(cancellation ?? CancellationToken.None);

                base.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());   // TODO FIXME !!!!
            }
        }

        public void Disconnect()
        {
            _stream.Disconnect();
        }

        public override void Dispose()
        {
            if (_stream.IsConnected)
                _stream.Disconnect();

            base.Dispose();
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);
    }
}
