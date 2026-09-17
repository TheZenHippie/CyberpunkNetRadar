using System;
using System.Net;

namespace CyberPunkNetRadar.Models
{
    public class NetworkEndpointModel
    {
        public string Key { get; set; } = string.Empty;
        public string RemoteIp { get; set; } = string.Empty;
        public int RemotePort { get; set; }
        public int LocalPort { get; set; }
        public string Protocol { get; set; } = "TCP";
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = "Unknown";
        public bool IsPrivateLan { get; set; }

        // Polar Radar Coordinates
        public float BearingDeg { get; set; } // 0 to 360 degrees
        public double LatencyMs { get; set; } = 15.0; // Current measured or estimated latency in ms
        public bool IsFallbackPing { get; set; } = false;

        // Lifecycle & Phosphor Decay
        public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
        public DateTime LastIlluminatedTime { get; set; } = DateTime.MinValue;
        public bool HasBeenSwept { get; set; } = false;

        // Cached Screen Coordinates for Hit-Testing & Hover Reticle
        public float ScreenX { get; set; }
        public float ScreenY { get; set; }
        public float NormalizedRadius { get; set; } // 0.0 (center) to 1.0 (perimeter)

        public float GetPhosphorIntensity(double fadeDurationSeconds)
        {
            if (!HasBeenSwept || LastIlluminatedTime == DateTime.MinValue)
            {
                // Unswept blips show at a faint standby glow (15% intensity)
                return 0.15f;
            }

            double elapsed = (DateTime.UtcNow - LastIlluminatedTime).TotalSeconds;
            if (elapsed < 0) elapsed = 0;
            if (elapsed >= fadeDurationSeconds)
            {
                // Retain a baseline 10% faint beacon so the connection is still trackable between sweeps
                return 0.10f;
            }

            // Exponential phosphor decay curve from 1.0 down to 0.10
            double progress = elapsed / fadeDurationSeconds;
            double decay = Math.Exp(-progress * 2.8); // Smooth natural CRT phosphor decay
            return (float)Math.Clamp(0.10 + decay * 0.90, 0.10, 1.0);
        }
    }

    public class RadarTelemetryStats
    {
        public int TotalActiveConnections { get; set; }
        public int TcpConnectionsCount { get; set; }
        public int UdpEndpointsCount { get; set; }
        public double MinPingMs { get; set; }
        public double MaxPingMs { get; set; }
        public double AvgPingMs { get; set; }
        public float CurrentSweepAngleDeg { get; set; }
        public double CurrentSweepPeriodSeconds { get; set; }
        public double CurrentMaxRangeMs { get; set; }
    }
}

