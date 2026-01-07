using MadWizard.Desomnia.Network.Knocking.Secrets;
using System.Net;

namespace MadWizard.Desomnia.Network.Configuration.Options
{
    public struct KnockOptions
    {
        public string           Method      { get; init; }
        public IPPort           Port        { get; init; }

        public TimeSpan         Delay       { get; init; }
        public TimeSpan?        Repeat      { get; init; }
        public TimeSpan         Timeout     { get; init; }

        public SharedSecret     Secret      { get; init; }
    }
}
