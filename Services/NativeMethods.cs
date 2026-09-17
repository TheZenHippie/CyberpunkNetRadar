using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace CyberPunkNetRadar.Services
{
    public static class NativeMethods
    {
        public const int AF_INET = 2;       // IPv4
        public const int AF_INET6 = 23;     // IPv6

        public enum TCP_TABLE_CLASS
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

        public enum UDP_TABLE_CLASS
        {
            UDP_TABLE_BASIC,
            UDP_TABLE_OWNER_PID,
            UDP_TABLE_OWNER_MODULE
        }

        public enum MIB_TCP_STATE
        {
            CLOSED = 1,
            LISTEN = 2,
            SYN_SENT = 3,
            SYN_RCVD = 4,
            ESTABLISHED = 5,
            FIN_WAIT1 = 6,
            FIN_WAIT2 = 7,
            CLOSE_WAIT = 8,
            CLOSING = 9,
            LAST_ACK = 10,
            TIME_WAIT = 11,
            DELETE_TCB = 12
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] localPort;
            public uint remoteAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] remotePort;
            public uint owningPid;

            public uint ProcessId => owningPid;
            public IPAddress LocalAddress => new IPAddress(localAddr);
            public ushort LocalPortNumber => (ushort)((localPort[0] << 8) + localPort[1]);
            public IPAddress RemoteAddress => new IPAddress(remoteAddr);
            public ushort RemotePortNumber => (ushort)((remotePort[0] << 8) + remotePort[1]);
            public MIB_TCP_STATE State => (MIB_TCP_STATE)state;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_TCP6ROW_OWNER_PID
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] localAddr;
            public uint localScopeId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] localPort;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] remoteAddr;
            public uint remoteScopeId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] remotePort;
            public uint state;
            public uint owningPid;

            public uint ProcessId => owningPid;
            public IPAddress LocalAddress => new IPAddress(localAddr, localScopeId);
            public ushort LocalPortNumber => (ushort)((localPort[0] << 8) + localPort[1]);
            public IPAddress RemoteAddress => new IPAddress(remoteAddr, remoteScopeId);
            public ushort RemotePortNumber => (ushort)((remotePort[0] << 8) + remotePort[1]);
            public MIB_TCP_STATE State => (MIB_TCP_STATE)state;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_UDPROW_OWNER_PID
        {
            public uint localAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] localPort;
            public uint owningPid;

            public uint ProcessId => owningPid;
            public IPAddress LocalAddress => new IPAddress(localAddr);
            public ushort LocalPortNumber => (ushort)((localPort[0] << 8) + localPort[1]);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_UDP6ROW_OWNER_PID
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] localAddr;
            public uint localScopeId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] localPort;
            public uint owningPid;

            public uint ProcessId => owningPid;
            public IPAddress LocalAddress => new IPAddress(localAddr, localScopeId);
            public ushort LocalPortNumber => (ushort)((localPort[0] << 8) + localPort[1]);
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern uint GetExtendedTcpTable(
            IntPtr pTcpTable,
            ref int pdwSize,
            bool bOrder,
            int ulAf,
            TCP_TABLE_CLASS TableClass,
            uint reserved = 0);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern uint GetExtendedUdpTable(
            IntPtr pUdpTable,
            ref int pdwSize,
            bool bOrder,
            int ulAf,
            UDP_TABLE_CLASS TableClass,
            uint reserved = 0);

        public static string GetProcessNameFromPid(int pid)
        {
            if (pid <= 0) return "System";
            if (pid == 4) return "System";

            try
            {
                using var proc = Process.GetProcessById(pid);
                return proc.ProcessName;
            }
            catch
            {
                return "Unknown";
            }
        }
    }
}

