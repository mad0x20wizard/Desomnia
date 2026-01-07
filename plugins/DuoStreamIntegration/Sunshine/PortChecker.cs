using MadWizard.Desomnia.Network.Neighborhood.Services;
using System.Net;
using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Service.Duo.Sunshine
{
    internal class PortChecker
    {
        private const int AF_INET = 2; // IPv4

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedUdpTable(nint pUdpTable, ref int pdwSize, bool bOrder, int ulAf, UDP_TABLE_CLASS TableClass, int Reserved);
        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern int GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, TCP_TABLE_CLASS TableClass, int Reserved);

        private enum UDP_TABLE_CLASS
        {
            OWNER_PID = 1
        }

        private enum TCP_TABLE_CLASS
        {
            TCP_TABLE_BASIC_LISTENER,
            TCP_TABLE_BASIC_CONNECTIONS,
            TCP_TABLE_BASIC_ALL,
            TCP_TABLE_OWNER_PID_LISTENER,
            TCP_TABLE_OWNER_PID_CONNECTIONS,
            TCP_TABLE_OWNER_PID_ALL,
            TCP_TABLE_OWNER_MODULE_LISTENER,
            TCP_TABLE_OWNER_MODULE_CONNECTIONS,
            TCP_TABLE_OWNER_MODULE_ALL
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPTABLE_OWNER_PID
        {
            public uint dwNumEntries;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint dwState;
            public uint dwLocalAddr;
            public uint dwLocalPort;
            public uint dwRemoteAddr;
            public uint dwRemotePort;
            public uint dwOwningPid;
        }

        private class TcpConnectionEntry(MIB_TCPROW_OWNER_PID row)
        {
            public IPAddress LocalAddress { get; } = new(row.dwLocalAddr);
            public ushort LocalPort => (ushort)IPAddress.NetworkToHostOrder((short)row.dwLocalPort);
            public IPAddress RemoteAddress { get; } = new(row.dwRemoteAddr);
            public ushort RemotePort => (ushort)IPAddress.NetworkToHostOrder((short)row.dwRemotePort);
            public uint ProcessId => row.dwOwningPid;
            public TcpState State => (TcpState)row.dwState;
        }

        private enum TcpState
        {
            CLOSED = 1,
            LISTEN = 2,
            SYN_SENT = 3,
            SYN_RECEIVED = 4,
            ESTABLISHED = 5,
            FIN_WAIT_1 = 6,
            FIN_WAIT_2 = 7,
            CLOSE_WAIT = 8,
            CLOSING = 9,
            LAST_ACK = 10,
            TIME_WAIT = 11,
            DELETE_TCB = 12
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UDP_TABLE_ROW
        {
            public int LocalAddr;
            public byte LocalPort1, LocalPort2, LocalPort3, LocalPort4;
            public int OwningPid;
        }

        internal static bool IsTCPPortInUse(params ushort[] ports)
        {
            var table = GetOpenTcpConnections().Where(conn => conn.State == TcpState.LISTEN || conn.State == TcpState.ESTABLISHED || conn.State == TcpState.CLOSE_WAIT);

            foreach (var entry in table)
            {
                if (ports.Contains(entry.LocalPort))
                    return true;

                //Console.WriteLine($"Local: {entry.LocalAddress}:{entry.LocalPort} -> Remote: {entry.RemoteAddress}:{entry.RemotePort} | PID: {entry.ProcessId} | State: {entry.State}");
            }

            return false;
        }

        private static List<TcpConnectionEntry> GetOpenTcpConnections()
        {
            List<TcpConnectionEntry> connections = [];

            int bufferSize = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
            IntPtr tcpTablePtr = Marshal.AllocHGlobal(bufferSize);

            try
            {
                if (GetExtendedTcpTable(tcpTablePtr, ref bufferSize, true, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0) == 0)
                {
                    MIB_TCPTABLE_OWNER_PID tcpTable = Marshal.PtrToStructure<MIB_TCPTABLE_OWNER_PID>(tcpTablePtr);
                    IntPtr rowPtr = (IntPtr)((long)tcpTablePtr + Marshal.SizeOf(tcpTable.dwNumEntries));

                    for (int i = 0; i < tcpTable.dwNumEntries; i++)
                    {
                        MIB_TCPROW_OWNER_PID row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                        connections.Add(new TcpConnectionEntry(row));
                        rowPtr = (IntPtr)((long)rowPtr + Marshal.SizeOf(row));
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(tcpTablePtr);
            }

            return connections;
        }


        internal static bool IsUDPPortInUse(params int[] ports)
        {
            int bufferSize = 0;
            uint result = GetExtendedUdpTable(nint.Zero, ref bufferSize, true, AF_INET, UDP_TABLE_CLASS.OWNER_PID, 0);

            nint tablePtr = Marshal.AllocHGlobal(bufferSize);
            try
            {
                if (GetExtendedUdpTable(tablePtr, ref bufferSize, true, AF_INET, UDP_TABLE_CLASS.OWNER_PID, 0) != 0)
                    return false;

                int tableSize = Marshal.ReadInt32(tablePtr);
                nint rowPtr = tablePtr + 4;

                for (int i = 0; i < tableSize; i++)
                {
                    UDP_TABLE_ROW row = Marshal.PtrToStructure<UDP_TABLE_ROW>(rowPtr);

                    int localPort = row.LocalPort1 << 8 | row.LocalPort2;

                    if (ports.Contains(localPort))
                        return true;

                    rowPtr += Marshal.SizeOf<UDP_TABLE_ROW>();
                }
            }
            finally
            {
                Marshal.FreeHGlobal(tablePtr);
            }
            return false;
        }

        internal static bool IsAnyPortInUse(params IPPort[] services)
        {
            foreach (var service in services)
            {
                switch (service.Protocol)
                {
                    case IPProtocol.TCP when IsTCPPortInUse(service.Port):
                        return true;
                    case IPProtocol.UDP when IsUDPPortInUse(service.Port):
                        return true;
                }
            }

            return false;
        }
    }
}