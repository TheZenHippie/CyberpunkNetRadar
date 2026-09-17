using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CyberPunkNetRadar.Models;

namespace CyberPunkNetRadar.Services
{
    public class NetworkMonitorService : IDisposable
    {
        private readonly ConcurrentDictionary<string, NetworkEndpointModel> _activeEndpoints = new();
        private readonly ConcurrentDictionary<string, (double Latency, DateTime Timestamp)> _pingCache = new();
        private readonly ConcurrentDictionary<int, string> _processNameCache = new();
        
        private readonly CancellationTokenSource _cts = new();
        private readonly SemaphoreSlim _pingThrottler = new(8, 8);
        private Task? _monitorTask;
        private bool _isDisposed;

        public event Action? EndpointsUpdated;

        public NetworkMonitorService()
        {
            _monitorTask = Task.Run(MonitorLoopAsync);
        }

        public IReadOnlyList<NetworkEndpointModel> GetActiveEndpointsSnapshot()
        {
            return _activeEndpoints.Values.ToList();
        }

        private async Task MonitorLoopAsync()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    ScanConnections();
                    EndpointsUpdated?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error scanning connections: {ex.Message}");
                }

                try
                {
                    await Task.Delay(1200, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void ScanConnections()
        {
            var currentKeys = new HashSet<string>();
            var scannedEndpoints = new List<NetworkEndpointModel>();

            // 1. Query IPv4 TCP Connections
            scannedEndpoints.AddRange(GetIpv4TcpConnections());

            // 2. Query IPv6 TCP Connections
            scannedEndpoints.AddRange(GetIpv6TcpConnections());

            // 3. Query IPv4 UDP Endpoints
            scannedEndpoints.AddRange(GetIpv4UdpEndpoints());

            // 4. Query IPv6 UDP Endpoints
            scannedEndpoints.AddRange(GetIpv6UdpEndpoints());

            DateTime now = DateTime.UtcNow;

            foreach (var ep in scannedEndpoints)
            {
                currentKeys.Add(ep.Key);

                if (_activeEndpoints.TryGetValue(ep.Key, out var existing))
                {
                    existing.LastSeen = now;
                    existing.ProcessId = ep.ProcessId;
                    existing.ProcessName = ep.ProcessName;
                    // Trigger async ping update if cache expired
                    QueuePingUpdate(existing);
                }
                else
                {
                    ep.FirstSeen = now;
                    ep.LastSeen = now;
                    // Assign deterministic bearing angle from IP
                    ep.BearingDeg = CalculateDeterministicAngle(ep.RemoteIp, ep.RemotePort);
                    _activeEndpoints[ep.Key] = ep;
                    QueuePingUpdate(ep);
                }
            }

            // Remove expired endpoints (not seen for > 4.5 seconds)
            var staleKeys = _activeEndpoints.Where(kvp => (now - kvp.Value.LastSeen).TotalSeconds > 4.5)
                                           .Select(kvp => kvp.Key)
                                           .ToList();

            foreach (var key in staleKeys)
            {
                _activeEndpoints.TryRemove(key, out _);
            }
        }

        private List<NetworkEndpointModel> GetIpv4TcpConnections()
        {
            var list = new List<NetworkEndpointModel>();
            int bufferSize = 0;
            
            // Get required buffer size
            NativeMethods.GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, NativeMethods.AF_INET, NativeMethods.TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL);
            if (bufferSize <= 0) return list;

            IntPtr pTable = Marshal.AllocHGlobal(bufferSize);
            try
            {
                uint result = NativeMethods.GetExtendedTcpTable(pTable, ref bufferSize, true, NativeMethods.AF_INET, NativeMethods.TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL);
                if (result == 0)
                {
                    int rowCount = Marshal.ReadInt32(pTable);
                    IntPtr rowPtr = IntPtr.Add(pTable, 4);
                    int rowSize = Marshal.SizeOf<NativeMethods.MIB_TCPROW_OWNER_PID>();

                    for (int i = 0; i < rowCount; i++)
                    {
                        var row = Marshal.PtrToStructure<NativeMethods.MIB_TCPROW_OWNER_PID>(rowPtr);
                        rowPtr = IntPtr.Add(rowPtr, rowSize);

                        if (row.State != NativeMethods.MIB_TCP_STATE.ESTABLISHED &&
                            row.State != NativeMethods.MIB_TCP_STATE.SYN_SENT &&
                            row.State != NativeMethods.MIB_TCP_STATE.CLOSE_WAIT)
                        {
                            continue;
                        }

                        var remoteIp = row.RemoteAddress;
                        if (IsLoopbackOrUnspecified(remoteIp)) continue;

                        string ipStr = remoteIp.ToString();
                        int rPort = row.RemotePortNumber;
                        int lPort = row.LocalPortNumber;
                        string key = $"TCP:{ipStr}:{rPort}";
                        int pid = (int)row.ProcessId;

                        list.Add(new NetworkEndpointModel
                        {
                            Key = key,
                            RemoteIp = ipStr,
                            RemotePort = rPort,
                            LocalPort = lPort,
                            Protocol = "TCP",
                            ProcessId = pid,
                            ProcessName = ResolveProcessName(pid),
                            IsPrivateLan = IsPrivateAddress(remoteIp)
                        });
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pTable);
            }

            return list;
        }

        private List<NetworkEndpointModel> GetIpv6TcpConnections()
        {
            var list = new List<NetworkEndpointModel>();
            int bufferSize = 0;

            NativeMethods.GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, NativeMethods.AF_INET6, NativeMethods.TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL);
            if (bufferSize <= 0) return list;

            IntPtr pTable = Marshal.AllocHGlobal(bufferSize);
            try
            {
                uint result = NativeMethods.GetExtendedTcpTable(pTable, ref bufferSize, true, NativeMethods.AF_INET6, NativeMethods.TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL);
                if (result == 0)
                {
                    int rowCount = Marshal.ReadInt32(pTable);
                    IntPtr rowPtr = IntPtr.Add(pTable, 4);
                    int rowSize = Marshal.SizeOf<NativeMethods.MIB_TCP6ROW_OWNER_PID>();

                    for (int i = 0; i < rowCount; i++)
                    {
                        var row = Marshal.PtrToStructure<NativeMethods.MIB_TCP6ROW_OWNER_PID>(rowPtr);
                        rowPtr = IntPtr.Add(rowPtr, rowSize);

                        if (row.State != NativeMethods.MIB_TCP_STATE.ESTABLISHED &&
                            row.State != NativeMethods.MIB_TCP_STATE.SYN_SENT &&
                            row.State != NativeMethods.MIB_TCP_STATE.CLOSE_WAIT)
                        {
                            continue;
                        }

                        var remoteIp = row.RemoteAddress;
                        if (IsLoopbackOrUnspecified(remoteIp)) continue;

                        string ipStr = remoteIp.ToString();
                        int rPort = row.RemotePortNumber;
                        int lPort = row.LocalPortNumber;
                        string key = $"TCP6:[{ipStr}]:{rPort}";
                        int pid = (int)row.ProcessId;

                        list.Add(new NetworkEndpointModel
                        {
                            Key = key,
                            RemoteIp = ipStr,
                            RemotePort = rPort,
                            LocalPort = lPort,
                            Protocol = "TCP6",
                            ProcessId = pid,
                            ProcessName = ResolveProcessName(pid),
                            IsPrivateLan = IsPrivateAddress(remoteIp)
                        });
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pTable);
            }

            return list;
        }

        private List<NetworkEndpointModel> GetIpv4UdpEndpoints()
        {
            var list = new List<NetworkEndpointModel>();
            int bufferSize = 0;

            NativeMethods.GetExtendedUdpTable(IntPtr.Zero, ref bufferSize, true, NativeMethods.AF_INET, NativeMethods.UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID);
            if (bufferSize <= 0) return list;

            IntPtr pTable = Marshal.AllocHGlobal(bufferSize);
            try
            {
                uint result = NativeMethods.GetExtendedUdpTable(pTable, ref bufferSize, true, NativeMethods.AF_INET, NativeMethods.UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID);
                if (result == 0)
                {
                    int rowCount = Marshal.ReadInt32(pTable);
                    IntPtr rowPtr = IntPtr.Add(pTable, 4);
                    int rowSize = Marshal.SizeOf<NativeMethods.MIB_UDPROW_OWNER_PID>();

                    for (int i = 0; i < rowCount; i++)
                    {
                        var row = Marshal.PtrToStructure<NativeMethods.MIB_UDPROW_OWNER_PID>(rowPtr);
                        rowPtr = IntPtr.Add(rowPtr, rowSize);

                        var localIp = row.LocalAddress;
                        int lPort = row.LocalPortNumber;
                        int pid = (int)row.ProcessId;

                        // Skip internal system listeners on all-zeros if desired, or show active UDP endpoints
                        if (lPort == 0) continue;

                        string ipStr = localIp.ToString();
                        string key = $"UDP:{ipStr}:{lPort}:{pid}";

                        list.Add(new NetworkEndpointModel
                        {
                            Key = key,
                            RemoteIp = ipStr == "0.0.0.0" ? "0.0.0.0 (UDP Listener)" : ipStr,
                            RemotePort = lPort,
                            LocalPort = lPort,
                            Protocol = "UDP",
                            ProcessId = pid,
                            ProcessName = ResolveProcessName(pid),
                            IsPrivateLan = IsPrivateAddress(localIp)
                        });
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pTable);
            }

            return list;
        }

        private List<NetworkEndpointModel> GetIpv6UdpEndpoints()
        {
            var list = new List<NetworkEndpointModel>();
            int bufferSize = 0;

            NativeMethods.GetExtendedUdpTable(IntPtr.Zero, ref bufferSize, true, NativeMethods.AF_INET6, NativeMethods.UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID);
            if (bufferSize <= 0) return list;

            IntPtr pTable = Marshal.AllocHGlobal(bufferSize);
            try
            {
                uint result = NativeMethods.GetExtendedUdpTable(pTable, ref bufferSize, true, NativeMethods.AF_INET6, NativeMethods.UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID);
                if (result == 0)
                {
                    int rowCount = Marshal.ReadInt32(pTable);
                    IntPtr rowPtr = IntPtr.Add(pTable, 4);
                    int rowSize = Marshal.SizeOf<NativeMethods.MIB_UDP6ROW_OWNER_PID>();

                    for (int i = 0; i < rowCount; i++)
                    {
                        var row = Marshal.PtrToStructure<NativeMethods.MIB_UDP6ROW_OWNER_PID>(rowPtr);
                        rowPtr = IntPtr.Add(rowPtr, rowSize);

                        var localIp = row.LocalAddress;
                        int lPort = row.LocalPortNumber;
                        int pid = (int)row.ProcessId;

                        if (lPort == 0) continue;

                        string ipStr = localIp.ToString();
                        string key = $"UDP6:[{ipStr}]:{lPort}:{pid}";

                        list.Add(new NetworkEndpointModel
                        {
                            Key = key,
                            RemoteIp = ipStr == "::" ? ":: (UDP6 Listener)" : ipStr,
                            RemotePort = lPort,
                            LocalPort = lPort,
                            Protocol = "UDP6",
                            ProcessId = pid,
                            ProcessName = ResolveProcessName(pid),
                            IsPrivateLan = IsPrivateAddress(localIp)
                        });
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pTable);
            }

            return list;
        }

        private void QueuePingUpdate(NetworkEndpointModel endpoint)
        {
            string ip = endpoint.RemoteIp;
            if (ip.StartsWith("0.0.0.0") || ip.StartsWith("::"))
            {
                // Local listener fallback latency
                endpoint.LatencyMs = 1.0;
                endpoint.IsFallbackPing = true;
                return;
            }

            if (_pingCache.TryGetValue(ip, out var cached))
            {
                if ((DateTime.UtcNow - cached.Timestamp).TotalSeconds < 5.0)
                {
                    endpoint.LatencyMs = cached.Latency;
                    return;
                }
            }

            // Probe asynchronously
            _ = Task.Run(async () =>
            {
                if (!await _pingThrottler.WaitAsync(200)) return;

                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(ip, 650);
                    if (reply.Status == IPStatus.Success)
                    {
                        double lat = Math.Max(1.0, reply.RoundtripTime);
                        endpoint.LatencyMs = lat;
                        endpoint.IsFallbackPing = false;
                        _pingCache[ip] = (lat, DateTime.UtcNow);
                    }
                    else
                    {
                        // Fallback estimated latency
                        double fallback = endpoint.IsPrivateLan ? 2.5 : CalculateFallbackPing(ip);
                        endpoint.LatencyMs = fallback;
                        endpoint.IsFallbackPing = true;
                        _pingCache[ip] = (fallback, DateTime.UtcNow);
                    }
                }
                catch
                {
                    double fallback = endpoint.IsPrivateLan ? 2.5 : CalculateFallbackPing(ip);
                    endpoint.LatencyMs = fallback;
                    endpoint.IsFallbackPing = true;
                    _pingCache[ip] = (fallback, DateTime.UtcNow);
                }
                finally
                {
                    _pingThrottler.Release();
                }
            });
        }

        private double CalculateFallbackPing(string ip)
        {
            // Deterministic synthetic fallback latency between 12ms and 28ms for endpoints that drop ICMP
            uint hash = ComputeFnv1aHash(ip);
            return 12.0 + (hash % 170) / 10.0;
        }

        public static float CalculateDeterministicAngle(string ip, int port)
        {
            // FNV-1a Hash mapped to [0, 360) degrees
            uint hash = ComputeFnv1aHash($"{ip}:{port % 64}");
            float angle = (hash % 36000) / 100.0f; // 0.00 to 359.99
            return angle;
        }

        private static uint ComputeFnv1aHash(string text)
        {
            const uint fnvPrime = 16777619;
            uint hash = 2166136261;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= fnvPrime;
            }
            return hash;
        }

        private string ResolveProcessName(int pid)
        {
            if (_processNameCache.TryGetValue(pid, out var cached))
            {
                return cached;
            }

            string name = NativeMethods.GetProcessNameFromPid(pid);
            _processNameCache[pid] = name;
            return name;
        }

        private static bool IsLoopbackOrUnspecified(IPAddress ip)
        {
            if (IPAddress.IsLoopback(ip)) return true;
            if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return true;
            if (ip.Equals(IPAddress.None) || ip.Equals(IPAddress.IPv6None)) return true;
            return false;
        }

        private static bool IsPrivateAddress(IPAddress ip)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] bytes = ip.GetAddressBytes();
                // 10.0.0.0/8
                if (bytes[0] == 10) return true;
                // 172.16.0.0/12
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168) return true;
                // 169.254.0.0/16 (Link-local)
                if (bytes[0] == 169 && bytes[1] == 254) return true;
            }
            else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal) return true;
            }
            return false;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _cts.Cancel();
            _pingThrottler.Dispose();
            _cts.Dispose();
        }
    }
}

