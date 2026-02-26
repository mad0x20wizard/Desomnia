using System.Numerics;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MadWizard.Desomnia.Network.Configuration.Options
{
    public readonly struct ClockOptions
    {
        public bool Remote { get; init; }
        public bool Disconnected { get; init; }

        public static ClockOptions operator +(ClockOptions left, ClockOptions right)
        {
            return new ClockOptions()
            {
                Disconnected = left.Disconnected || right.Disconnected,
                Remote = left.Remote || right.Remote
            };
        }
    }
}
