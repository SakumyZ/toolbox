using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace ToolBox.Services
{
    /// <summary>
    /// 端口连接信息
    /// </summary>
    public class PortEntry
    {
        public string Protocol { get; set; } = "";
        public string LocalAddress { get; set; } = "";
        public int LocalPort { get; set; }
        public string RemoteAddress { get; set; } = "";
        public int RemotePort { get; set; }
        public string State { get; set; } = "";
        public int Pid { get; set; }
        public string ProcessName { get; set; } = "";
        public string ProcessPath { get; set; } = "";
    }

    /// <summary>
    /// 端口信息服务，通过 Win32 API 获取 TCP/UDP 连接和对应进程
    /// </summary>
    public static class PortService
    {
        // ========== Win32 API 定义 ==========

        private const int AF_INET = 2;

        private enum TcpTableClass
        {
            TCP_TABLE_OWNER_PID_ALL = 5
        }

        private enum UdpTableClass
        {
            UDP_TABLE_OWNER_PID = 1
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

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_UDPROW_OWNER_PID
        {
            public uint dwLocalAddr;
            public uint dwLocalPort;
            public uint dwOwningPid;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr pTcpTable, ref int pdwSize, bool bOrder,
            int ulAf, TcpTableClass tableClass, uint reserved);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedUdpTable(
            IntPtr pUdpTable, ref int pdwSize, bool bOrder,
            int ulAf, UdpTableClass tableClass, uint reserved);

        private static readonly string[] TcpStates = new[]
        {
            "", "CLOSED", "LISTEN", "SYN_SENT", "SYN_RCVD",
            "ESTABLISHED", "FIN_WAIT1", "FIN_WAIT2", "CLOSE_WAIT",
            "CLOSING", "LAST_ACK", "TIME_WAIT", "DELETE_TCB"
        };

        /// <summary>
        /// 常用开发端口集合
        /// </summary>
        public static readonly HashSet<int> CommonDevPorts = new()
        {
            21, 22, 25, 53, 80, 443, 445, 1433, 1521,
            3000, 3306, 3389, 4200, 5000, 5173, 5432, 5500,
            6379, 8000, 8080, 8443, 8888, 9090, 9200, 9300,
            27017, 44321, 44322
        };

        // 进程名缓存（避免频繁查询同一 PID）
        private static readonly Dictionary<int, (string name, string path)> _processCache = new();

        /// <summary>
        /// 获取所有端口连接信息
        /// </summary>
        public static List<PortEntry> GetAllPorts()
        {
            _processCache.Clear();
            var entries = new List<PortEntry>();

            entries.AddRange(GetTcpPorts());
            entries.AddRange(GetUdpPorts());

            return entries;
        }

        /// <summary>
        /// 获取仅监听状态的端口
        /// </summary>
        public static List<PortEntry> GetListeningPorts()
        {
            _processCache.Clear();
            var entries = new List<PortEntry>();

            foreach (var entry in GetTcpPorts())
            {
                if (entry.State == "LISTEN")
                {
                    entries.Add(entry);
                }
            }

            foreach (var entry in GetUdpPorts())
            {
                entries.Add(entry); // UDP 没有状态，全部算"监听"
            }

            return entries;
        }

        /// <summary>
        /// 结束指定 PID 的进程
        /// </summary>
        public static bool KillProcess(int pid)
        {
            try
            {
                var process = Process.GetProcessById(pid);
                process.Kill();
                process.WaitForExit(3000);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ========== TCP ==========

        private static List<PortEntry> GetTcpPorts()
        {
            var entries = new List<PortEntry>();
            int size = 0;

            // 第一次调用获取所需缓冲区大小
            GetExtendedTcpTable(IntPtr.Zero, ref size, true, AF_INET, TcpTableClass.TCP_TABLE_OWNER_PID_ALL, 0);

            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                uint result = GetExtendedTcpTable(buffer, ref size, true, AF_INET, TcpTableClass.TCP_TABLE_OWNER_PID_ALL, 0);
                if (result != 0) return entries;

                // 第一个 DWORD 是行数
                int rowCount = Marshal.ReadInt32(buffer);
                var rowPtr = buffer + 4;
                int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

                for (int i = 0; i < rowCount; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);

                    var (procName, procPath) = GetProcessInfo((int)row.dwOwningPid);
                    var state = row.dwState < TcpStates.Length ? TcpStates[row.dwState] : "UNKNOWN";

                    entries.Add(new PortEntry
                    {
                        Protocol = "TCP",
                        LocalAddress = new IPAddress(row.dwLocalAddr).ToString(),
                        LocalPort = NetworkToHostPort(row.dwLocalPort),
                        RemoteAddress = new IPAddress(row.dwRemoteAddr).ToString(),
                        RemotePort = NetworkToHostPort(row.dwRemotePort),
                        State = state,
                        Pid = (int)row.dwOwningPid,
                        ProcessName = procName,
                        ProcessPath = procPath
                    });

                    rowPtr += rowSize;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return entries;
        }

        // ========== UDP ==========

        private static List<PortEntry> GetUdpPorts()
        {
            var entries = new List<PortEntry>();
            int size = 0;

            GetExtendedUdpTable(IntPtr.Zero, ref size, true, AF_INET, UdpTableClass.UDP_TABLE_OWNER_PID, 0);

            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                uint result = GetExtendedUdpTable(buffer, ref size, true, AF_INET, UdpTableClass.UDP_TABLE_OWNER_PID, 0);
                if (result != 0) return entries;

                int rowCount = Marshal.ReadInt32(buffer);
                var rowPtr = buffer + 4;
                int rowSize = Marshal.SizeOf<MIB_UDPROW_OWNER_PID>();

                for (int i = 0; i < rowCount; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);

                    var (procName, procPath) = GetProcessInfo((int)row.dwOwningPid);

                    entries.Add(new PortEntry
                    {
                        Protocol = "UDP",
                        LocalAddress = new IPAddress(row.dwLocalAddr).ToString(),
                        LocalPort = NetworkToHostPort(row.dwLocalPort),
                        RemoteAddress = "*",
                        RemotePort = 0,
                        State = "-",
                        Pid = (int)row.dwOwningPid,
                        ProcessName = procName,
                        ProcessPath = procPath
                    });

                    rowPtr += rowSize;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return entries;
        }

        // ========== 工具方法 ==========

        private static int NetworkToHostPort(uint networkPort)
        {
            // 端口号是网络字节序（大端），取高 2 字节
            return (int)((networkPort & 0xFF) << 8 | (networkPort & 0xFF00) >> 8);
        }

        private static (string name, string path) GetProcessInfo(int pid)
        {
            if (pid == 0) return ("System Idle", "");
            if (pid == 4) return ("System", "");

            if (_processCache.TryGetValue(pid, out var cached))
            {
                return cached;
            }

            try
            {
                var proc = Process.GetProcessById(pid);
                string name = proc.ProcessName;
                string path = "";
                try
                {
                    path = proc.MainModule?.FileName ?? "";
                }
                catch
                {
                    // 权限不足无法获取路径
                }
                var info = (name, path);
                _processCache[pid] = info;
                return info;
            }
            catch
            {
                var info = ($"[PID {pid}]", "");
                _processCache[pid] = info;
                return info;
            }
        }
    }
}
