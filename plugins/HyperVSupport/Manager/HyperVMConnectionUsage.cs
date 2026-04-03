using MadWizard.Desomnia.Network.Usage;

namespace MadWizard.Desomnia.Network.HyperV.Manager
{
    internal class HyperVMConnectionUsage(HyperVVirtualMachine vm, System.Diagnostics.Process process) : VirtualMachineUsage
    {
        public HyperVVirtualMachine VirtualMachine => vm;

        public override string ToString()
        {
            return $"{process.ProcessName} ({process.Id})";
        }
    }
}
