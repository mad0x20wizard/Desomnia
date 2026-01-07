using MadWizard.Desomnia.Pipe.Messages;
using MessagePack;
using MessagePack.Resolvers;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace MadWizard.Desomnia.Pipe
{
    // https://github.com/MessagePack-CSharp/MessagePack-CSharp

    public abstract class MessagePipe
    {
        public static readonly string PIPE_NAME = "DesomniaSessionBridge";

        public static string PipeNameBySessionId(uint sid) => $"{PIPE_NAME}#{sid}";
    }

    public abstract class MessagePipe<TPipeStream> : MessagePipe, IDisposable where TPipeStream : PipeStream 
    {
        protected TPipeStream _stream;

        private byte[] _buffer = new byte[1024 * 1024];

        private BufferBlock<Message> _queueIncoming = new BufferBlock<Message>();
        private BufferBlock<Message> _queueOutgoing = new BufferBlock<Message>();

        protected MessagePipe(TPipeStream stream)
        {
            _stream = stream;
        }

        public MessagePackSerializerOptions Options { get; set; } = TypelessContractlessStandardResolver.Options;

        public event EventHandler Connected;
        public event EventHandler<Message> MessageReceived;
        public event EventHandler<Exception> Error; // TODO !!!
        public event EventHandler Disconnected;

        protected virtual async void Start()
        {
            Connected?.Invoke(this, EventArgs.Empty);

            StartReceivingMessages();
            StartSendingMessages();

            while (true)
            {
                try
                {
                    var message = await _queueIncoming.ReceiveAsync();

                    MessageReceived?.Invoke(this, message);
                }
                catch (InvalidOperationException)
                {
                    break;
                }
            }

            _queueOutgoing.Complete();

            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        private async void StartReceivingMessages()
        {
            while (_stream.IsConnected)
            {
                _stream.ReadMode = PipeTransmissionMode.Message;

                int read = await _stream.ReadAsync(_buffer, 0, _buffer.Length);

                if (read > 0)
                {
                    var data = new Memory<byte>(_buffer, 0, read);

                    var message = MessagePackSerializer.Deserialize<object>(data, Options) as Message;

                    _queueIncoming.Post(message);
                }
                else
                    break;
            }

            _queueIncoming.Complete();
        }

        private async void StartSendingMessages()
        {
            while (_stream.IsConnected)
            {
                try
                { 
                    var message = await _queueOutgoing.ReceiveAsync();

                    byte[] bytes = MessagePackSerializer.Serialize(message, Options);

                    await _stream.WriteAsync(bytes, 0, bytes.Length);
                }
                catch (InvalidOperationException)
                {
                    break;
                }
            }
        }

        public void SendMessage(Message message)
        {
            _queueOutgoing.Post(message);
        }

        public virtual void Dispose()
        {
            _stream.Dispose();
            _stream.Close();
        }
    }
}
