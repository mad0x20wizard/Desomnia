using MadWizard.Desomnia.Network.Manager;

namespace MadWizard.Desomnia.Network.HyperV.Manager
{
    internal enum HyperVVMState : ushort
    {
        ///<summary>The state of the virtual machine could not be determined.</summary>
        Unknown = 0,

        ///<summary>The virtual machine is in an other state.</summary>
        Other = 1,

        ///<summary>The virtual machine is running.</summary>
        Running = 2,

        ///<summary>The virtual machine is turned off.</summary>
        Off = 3,

        ///<summary>The virtual machine is in the process of turning off.</summary>
        ShuttingDown = 4,

        ///<summary>The virtual machine does not support being started or turned off.</summary>
        NotApplicable = 5,

        ///<summary>The virtual machine might be completing commands, and it will drop any new requests.</summary>
        EnabledButOffline = 6,

        ///<summary>The virtual machine is in a test state.</summary>
        InTest = 7,

        ///<summary>The virtual machine might be completing commands, but it will queue any new requests.</summary>
        Deferred = 8,

        ///<summary>The virtual machine is running but in a restricted mode. The behavior of the virtual machine is similar to the Running state, but it processes only a restricted set of commands. All other requests are queued.</summary>
        Quiesce = 9,

        ///<summary>The virtual machine is in the process of starting. New requests are queued.</summary>
        Starting = 10
    }

    internal enum RequestedHyperVVMState : ushort
    {
        Other = 1,
        Enabled = 2,
        Disabled = 3,
        ShutDown = 4,
        Offline = 6,
        Test = 7,
        Defer = 8,
        Quiesce = 9,
        Reboot = 10,
        Reset = 11,

        Saving = 32773,
        Pausing = 32776,
        Resuming = 32777,
        FastSaved = 32779,
        FastSaving = 32780,
        RunningCritical = 32781,
        OffCritical = 32782,
        StoppingCritical = 32783,
        SavedCritical = 32784,
        PausedCritical = 32785,
        StartingCritical = 32786,
        ResetCritical = 32787,
        SavingCritical = 32788,
        PausingCritical = 32789,
        ResumingCritical = 32790,
        FastSavedCritical = 32791,
        FastSavingCritical = 32792
    }

    internal static class HyperVMStateHelper
    {
        internal static VirtualMachineState ToVMState(this HyperVVMState state) => state switch
        {
            HyperVVMState.Running => VirtualMachineState.Running,
            HyperVVMState.Off => VirtualMachineState.Stopped,
            HyperVVMState.EnabledButOffline => VirtualMachineState.Suspended,

            _ => VirtualMachineState.Unknown
        };
    }
}
