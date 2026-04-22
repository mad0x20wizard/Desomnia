using MadWizard.Desomnia.Network.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Context.Watch
{
    internal class PromiscuousModeMutex : INetworkService
    {
        public required ILogger<PromiscuousModeMutex> Logger { private get; init; }

        public required IHostApplicationLifetime Lifetime { private get; init; }

        public required NetworkDevice Device { private get; init; }

        public required TimeSpan Timeout { private get; init; }

        private DateTime _probedAuthorityTime;
        private bool? _establishdAuthority;

        async void INetworkService.Startup()
        {
            _probedAuthorityTime = DateTime.Now;

            SendBeacon();

            await Task.Delay(Timeout);

            if (_establishdAuthority ??= true)
            {
                Logger.LogDebug("Successfully established authority for promiscuous mode.");
            }
        }

        void INetworkService.ProcessPacket(EthernetPacket packet)
        {
            if (packet.IsMagicPacket(out var mac) && mac.Equals(PhysicalAddressExt.Broadcast))
            {
                if (_establishdAuthority == true)
                {
                    Logger.LogWarning("Another instance of Desomnia probed authority for promiscuous mode at: {mac}", packet.SourceHardwareAddress.ToHexString());

                    SendBeacon();
                }
                else if (_establishdAuthority == null)
                {
                    if (DateTime.Now - _probedAuthorityTime < Timeout)
                    {
                        _establishdAuthority = false;

                        Logger.LogCritical("Only one instance of Desomnia may be monitoring in promiscuous mode per broadcast domain; detected another instance at: {mac}", packet.SourceHardwareAddress.ToHexString());

                        Lifetime.StopApplication();
                    }
                }
            }
        }

        private void SendBeacon()
        {
            Logger.LogTrace("Advertising presence...");

            Device.SendPacket(new EthernetPacket(Device.PhysicalAddress, PhysicalAddressExt.Broadcast, EthernetType.WakeOnLan)
            {
                PayloadPacket = new WakeOnLanPacket(PhysicalAddressExt.Broadcast)
            });
        }
    }
}
