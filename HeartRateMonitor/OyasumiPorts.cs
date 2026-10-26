using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace HeartRateMonitor
{
    // Local IPv4 listeners owned by OyasumiVR; no network or port-range scan.
    internal static class OyasumiPorts
    {
        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
        internal static List<int> Find()
        {
            var pids = new HashSet<int>();
            foreach (var process in Process.GetProcesses()) using (process) {
                try { if (process.ProcessName.StartsWith("OyasumiVR", StringComparison.OrdinalIgnoreCase)) pids.Add(process.Id); }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
            }
            if (pids.Count == 0) return new List<int>();
            return FindForProcessIds(pids);
        }
        internal static List<int> FindForProcessIds(HashSet<int> pids)
        {
            int size = 0;
            uint rc = GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
            for (int attempt = 0; attempt < 3; attempt++) {
                if (rc != 122 && rc != 0) throw new Win32Exception((int)rc);
                if (size < 4 || size > 16 * 1024 * 1024) throw new InvalidOperationException("Invalid TCP listener table size.");
                int allocated = size; IntPtr table = Marshal.AllocHGlobal(allocated);
                try {
                    rc = GetExtendedTcpTable(table, ref size, false, 2, 3, 0);
                    if (rc == 122) continue;
                    if (rc != 0) throw new Win32Exception((int)rc);
                    int count = Marshal.ReadInt32(table);
                    if (count < 0 || count > (allocated - 4) / 24) throw new InvalidOperationException("Invalid TCP listener count.");
                    var ports = new HashSet<int>();
                    for (int i = 0; i < count; i++) {
                        IntPtr row = IntPtr.Add(table, 4 + i * 24);
                        int address = Marshal.ReadInt32(row, 4), pid = Marshal.ReadInt32(row, 20);
                        if (!pids.Contains(pid) || (address != 0 && address != 0x0100007f)) continue;
                        int port = (Marshal.ReadByte(row, 8) << 8) | Marshal.ReadByte(row, 9);
                        if (port > 0) ports.Add(port);
                    }
                    return ports.OrderBy(port => port).ToList();
                } finally { Marshal.FreeHGlobal(table); }
            }
            throw new InvalidOperationException("TCP listeners changed repeatedly. Will retry.");
        }
    }
}
