namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public class HTTPFilterRule : TCPServiceFilterRule
    {
        public HTTPFilterRule(ushort port) : base(port)
        {

        }

        public List<HTTPRequestFilterRule> RequestRules { get; set; } = [];

        //public required IDemandRequestData<byte[]?> Payload { private get; init; }

        protected override bool MatchesPayload(byte[] payload)
        {
            //if (Payload.Data == null)
            //{
            //    Payload.Data = payload;

            //    throw new ServicePayloadNeededException(Port);
            //}

            return true;
        }

        //private HTTPRequest Convert(byte[] payload)
        //{
        //    string text = Encoding.Default.GetString(payload);
        //    string[] lines = Regex.Split(text, "\r\n|\r|\n");
        //    string first = lines[0];
        //    string[] parts = first.Split(' ');

        //    if (parts.Length > 2)
        //    {
        //        return new HTTPRequest()
        //        {
        //            Method = parts[0],
        //            Path = parts[1],
        //            Version = parts[2],
        //        };
        //    }
        //    else
        //    {
        //        throw new ArgumentException("Invalid HTTP request format"); // LATER HTTPRequestFilter still hast to be implemented
        //    }
        //}
    }
}
