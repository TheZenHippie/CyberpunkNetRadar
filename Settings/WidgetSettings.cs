using System;
using System.IO;
using System.Text.Json;

namespace CyberPunkNetRadar.Settings
{
    public class WidgetSettings
    {
        // Sweep & Radar Settings
        public double SweepDurationSeconds { get; set; } = 10.0;
        public double BlipFadeDurationSeconds { get; set; } = 5.0;
        public double MaxRangeMs { get; set; } = 30.0; // 0 for Auto-Scale, or 30, 60, 100, 300
        public bool IsAutoScaleRange { get; set; } = false;

        // Color & Palette Settings
        public string AccentColorHex { get; set; } = "#00F0FF";
        public bool RgbSpectrumCycle { get; set; } = false;
        public double RgbCycleSpeed { get; set; } = 1.0;

        // Visual Display Settings
        public double WindowGlassOpacity { get; set; } = 0.90;
        public double LedBrightness { get; set; } = 1.0;
        public bool ShowBlipLabels { get; set; } = true;
        public bool ShowGridCoordinates { get; set; } = true;
        public bool ShowDigitalTelemetry { get; set; } = true;

        // Effects Suite
        public bool RainbowBorderEnabled { get; set; } = false;
        public double BorderWidth { get; set; } = 4.0;

        public bool CrtScanlinesEnabled { get; set; } = false;
        public double ScanlineThickness { get; set; } = 3.0;

        public bool CrtGlitchEnabled { get; set; } = false;
        public double GlitchChance { get; set; } = 15.0;

        public bool CrtSnowEnabled { get; set; } = false;
        public double SnowAmount { get; set; } = 25.0;

        // Window Position & State
        public bool AlwaysOnTop { get; set; } = false;
        public bool WindowShadow { get; set; } = true;

        public double? WindowWidth { get; set; } = 460;
        public double? WindowHeight { get; set; } = 460;
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private static string GetSettingsFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "CyberPunkNetRadar");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            return Path.Combine(folder, "settings.json");
        }

        public static WidgetSettings Load()
        {
            try
            {
                string path = GetSettingsFilePath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var settings = JsonSerializer.Deserialize<WidgetSettings>(json, JsonOptions);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch { }

            return new WidgetSettings();
        }

        public void Save()
        {
            try
            {
                string path = GetSettingsFilePath();
                string json = JsonSerializer.Serialize(this, JsonOptions);
                File.WriteAllText(path, json);
            }
            catch { }
        }
    }
}

