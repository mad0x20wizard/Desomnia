using MadWizard.Desomnia.Network.Datagram;
using MadWizard.Desomnia.Network.Naming.Options;
using Makaretu.Dns;

namespace MadWizard.Desomnia.Network.Naming
{
    public class DNSUpdate : DNSQuery
    {
        internal DNSUpdate(DatagramPacket packet, Message message) : base(packet, message)
        {
            Response = new Message { Id = message.Id, QR = true, Opcode = MessageOperation.Update };
        }

        internal void AnswerWithLease(TimeSpan duration)
        {
            var opt = new OPTRecord();
            opt.Options.Add(new EdnsLeaseOption { Duration = duration });
            Response.AdditionalRecords.Add(opt);
        }

        internal void AnswerWithError(MessageStatus status)
        {
            Response.AdditionalRecords.Clear();

            Response.Status = status;
        }
    }
}
