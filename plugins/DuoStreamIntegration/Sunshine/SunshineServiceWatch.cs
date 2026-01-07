using MadWizard.Desomnia.Network;

namespace MadWizard.Desomnia.Service.Duo.Sunshine
{
    public class SunshineServiceWatch(ushort port) : NetworkServiceWatch(new SunshineService(port))
    {
        public override bool IsHidden => true;

        public new SunshineService Service => (SunshineService)base.Service;
    }
}
