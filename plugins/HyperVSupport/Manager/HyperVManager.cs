using Autofac;
using Autofac.Features.OwnedInstances;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.Session.Manager;
using Microsoft.Extensions.Logging;
using Microsoft.Management.Infrastructure;
using System.Net;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Network.HyperV.Manager
{
    internal class HyperVManager : IVirtualMachineManager, IDisposable
    {
        internal const string NS = @"root/virtualization/v2";
        internal const string DIALECT = "WQL";

        public required ILogger<HyperVManager> Logger { internal get; init; }

        public required Func<string, HyperVVirtualMachine>  CreateVirtualMachine { private get; init; }
        public required Func<CimInstance, Owned<HyperVJob>> CreateJob { private get; init; }

        internal CimSession Session
        {
            get
            {
                if (field == null || !field.TestConnection())
                {
                    field?.Dispose();
                    field = null;

                    field = CimSession.Create(null);
                }

                return field;
            }

            set
            {
                if (value == null)
                {
                    field?.Dispose();
                    field = value;
                }
            }
        }

        internal Dictionary<string, HyperVVirtualMachine> Machines
        {
            get
            {
                if (field == null)
                {
                    field = new Dictionary<string, HyperVVirtualMachine>(StringComparer.OrdinalIgnoreCase);

                    LoadMachines();
                }

                return field;
            }
        }

        public IVirtualMachine? this[string name] => Machines.TryGetValue(name, out var machine) ? machine : null;

        private void LoadMachines()
        {
            const string QUERY = "SELECT Name, ElementName FROM Msvm_ComputerSystem WHERE Description = 'Microsoft Virtual Machine'";

            Logger.LogDebug($"Enumerating virtual machines:");

            foreach (var inst in Session.QueryInstances(NS, DIALECT, QUERY))
            {
                string guid = (string)inst.CimInstanceProperties["Name"].Value;
                string name = (string)inst.CimInstanceProperties["ElementName"].Value;

                var vm = CreateVirtualMachine(guid);

                Logger.LogDebug(vm.ToString());

                Machines[name] = vm;
            }
        }

        internal async Task RequestStateChange(HyperVVirtualMachine vm, RequestedHyperVVMState requested)
        {
            var instance = vm.QueryInstance(); // Msvm_ComputerSystem

            // RequestStateChange(uint16 requestedState)
            var result = Session.InvokeMethod(NS, instance, "RequestStateChange",
            [
                CimMethodParameter.Create("RequestedState", (ushort)requested, CimFlags.None)
            ]);

            if (result.ReturnValue.Value is uint rc)
            {
                switch (rc)
                {
                    case 0: return; // Completed with No Error

                    case 4096 when result.OutParameters["Job"]?.Value is CimInstance jobRef: // Method Parameters Checked - Transition Started
                    {
                        using var job = CreateJob(jobRef);

                        await job.Value.WaitForCompletion();

                        vm.State = vm.QueryState();

                        break;
                    }

                    default: throw new InvalidOperationException($"RequestStateChange({requested}) -> {ErrorCodeMeaning(rc)} ({rc})");
                }
            }

            static string ErrorCodeMeaning(uint rc) => rc switch
            {
                1 => "Not Supported",
                2 => "Failed",
                3 => "Timeout",
                4 => "Invalid Parameter",
                5 => "Invalid State",
                6 => "Invalid Type",

                32768 => "Failed",
                32769 => "Access Denied",
                32770 => "Not Supported",
                32771 => "Status is Unknown",
                32772 => "Timeout",
                32773 => "Invalid Parameter",
                32774 => "System is In Use",
                32775 => "Invalid State for this Operation",
                32776 => "Incorrect Data Type",
                32777 => "System is Not Available",
                32778 => "Out of Memory",

                _ => "Unknown Error",
            };
        }

        public IEnumerator<IVirtualMachine> GetEnumerator() => Machines.Values.GetEnumerator();

        void IDisposable.Dispose()
        {
            Session = null!;
        }
    }
}
