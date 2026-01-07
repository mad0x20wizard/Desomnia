using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MadWizard.Desomnia.Pipe
{
    public class MessagePipeClient : MessagePipe<NamedPipeClientStream>
    {
        public MessagePipeClient(uint sid)
            : base(new NamedPipeClientStream(".", PipeNameBySessionId(sid),
                PipeDirection.InOut,
                PipeOptions.Asynchronous |
                PipeOptions.WriteThrough))
        {

        }

        public async Task Connect(CancellationToken? cancellation = null)
        {
            await _stream.ConnectAsync(cancellation ?? CancellationToken.None);

            base.Start();
        }
    }
}
