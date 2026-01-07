using System.Net;
using System.Security.Cryptography;

namespace MadWizard.Desomnia.Network.FirewallKnockOperator
{
    internal class FKOData
    {
        static readonly char[] NUMBERS = ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9'];

        private string Random { get; init; }

        public string Username { get; init; } = "desomnia";
        public DateTimeOffset Timestamp { get; private init; }
        public string Version { get; init; } = "3.0.0";
        public MessageType Type { get; init; } = MessageType.Access;

        public IPAddress SourceAddress { get; init; }
        public IPPort? TargetPort { get; init; }

        public byte[]? Digest { get; set; }

        public FKOData(string plaintext)
        {
            var split = plaintext.Split(':');

            Random = split[0];
            Username = FKO.DecodeBase64Str(split[1]);
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(long.Parse(split[2]));
            Version = split[3];
            Type = (MessageType)uint.Parse(split[4]);

            var splitMessage = FKO.DecodeBase64Str(split[5]).Split(',');
            SourceAddress = IPAddress.Parse(splitMessage[0]);

            if (splitMessage.Length > 1 && splitMessage[1] is string port)
            {
                var splitPort = port.Split('/');

                var nr = ushort.Parse(splitPort[1]);
                TargetPort = splitPort[0].ToLower() switch
                {
                    "tcp" => (IPPort?)new IPPort(IPProtocol.TCP, nr),
                    "udp" => (IPPort?)new IPPort(IPProtocol.UDP, nr),

                    _ => throw new ArgumentException("Invalid protocol in FKO string"),
                };
            }

            Digest = split.Length > 6 ? FKO.DecodeBase64(split[6]) : null;
        }

        public FKOData(IPAddress source, IPPort? target)
        {
            Random = string.Join(null, RandomNumberGenerator.GetItems(NUMBERS, 16));

            Timestamp = DateTimeOffset.Now;

            SourceAddress = source;
            TargetPort = target;
        }

        private string Message
        {
            get
            {
                string message = $"{SourceAddress}";

                if (TargetPort is IPPort port)
                {
                    message += $",{port.Protocol.ToString().ToLower()}/{port.Port}";
                }

                return message;
            }
        }

        public string ToMessageString()
        {
            string plaintext = "";

            plaintext += Random;
            plaintext += ":";
            plaintext += FKO.EncodeBase64Str(Username);
            plaintext += ":";
            plaintext += Timestamp.ToUnixTimeSeconds();
            plaintext += ":";
            plaintext += Version;
            plaintext += ":";
            plaintext += (uint)Type;
            plaintext += ":";
            plaintext += FKO.EncodeBase64Str(Message);

            return plaintext;
        }

        public override string ToString()
        {
            var str = ToMessageString();

            if (Digest is not null)
            {
                str += $":{FKO.EncodeBase64(Digest)}";
            }

            return str;
        }

        internal enum MessageType : uint
        {
            Command = 0,
            Access = 1,
        }
    }
}
