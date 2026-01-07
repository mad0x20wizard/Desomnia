using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Manager
{
    public interface IVirtualMachineManager : IIEnumerable<IVirtualMachine>
    {
        public IVirtualMachine? this[string name] { get; }
    }

    public interface IVirtualMachine
    {
        public string Name { get; }

        public PhysicalAddress Address { get; }

        public VirtualMachineState State { get; }

        public event EventHandler<VirtualMachineStateChangedEventArgs> StateChanged;

        public Task Start();
        public Task Suspend();
        public Task Stop();
    }

    public enum VirtualMachineState
    {
        Unknown = 0,

        Running,
        Suspended,
        Stopped,
    }

    public class VirtualMachineStateChangedEventArgs(VirtualMachineState previous, VirtualMachineState state) : EventArgs
    {
        public VirtualMachineState PreviousState { get; } = previous;
        public VirtualMachineState State { get; } = state;
    }

    internal class CompositeVirtualMachineManager(IEnumerable<IVirtualMachineManager> managers) : IVirtualMachineManager
    {
        public IVirtualMachine? this[string name]
        {
            get
            {
                foreach (var manager in managers)
                {
                    if (manager[name] is IVirtualMachine vm)
                    {
                        return vm;
                    }
                }

                return null;
            }
        }

        public IEnumerator<IVirtualMachine> GetEnumerator()
        {
            return managers.SelectMany(m => m).GetEnumerator();
        }
    }
}
