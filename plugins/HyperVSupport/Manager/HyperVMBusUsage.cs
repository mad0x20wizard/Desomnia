using MadWizard.Desomnia.Network.Usage;

namespace MadWizard.Desomnia.Network.HyperV.Manager
{
    internal class HyperVMBusUsage(HyperVM vm, System.Diagnostics.Process process) : VirtualMachineUsage
    {
        public HyperVM VirtualMachine => vm;

        public override string ToString()
        {
            return $"{process.ProcessName} ({process.Id})";
        }
    }
}
