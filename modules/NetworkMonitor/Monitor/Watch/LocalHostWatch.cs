using MadWizard.Desomnia.Network.Neighborhood;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network
{
    public class LocalHostWatch : HostDemandWatch
    {
        public override bool IsOnline => true; // the local host is always available

        protected override bool ShouldStartRequest(EthernetPacket packet)
        {
            if (base.ShouldStartRequest(packet))
            {
                return !IsOnline || packet.IsIPUnicast(); // if host is online, only consider Address traffic
            }

            return false;
        }

        internal protected virtual void YieldWatch()
        {
            SendUnMagicPacket(Host);

            // SOMEDAY: Implement SleepProxy client protocol
        }

        private void SendUnMagicPacket(NetworkHost host)
        {
            if (host.PhysicalAddress is PhysicalAddress phy)
            {
                Logger.LogTrace($"Send UnMagic Packet for '{host.Name}' at {phy.ToHexString()}");

                Device.SendPacket(new EthernetPacket(phy, PhysicalAddressExt.Broadcast, EthernetType.WakeOnLan)
                {
                    PayloadPacket = new WakeOnLanPacket(phy)
                });
            }
        }
    }
}