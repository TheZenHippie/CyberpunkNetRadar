using System;
using SkiaSharp;

namespace CyberPunkNetRadar.Rendering
{
    public static class RadarPalette
    {
        // Standard Cyberpunk Palette Presets
        public static readonly SKColor ElectricCyan = new(0x00, 0xF0, 0xFF);     // #00F0FF
        public static readonly SKColor MatrixGreen = new(0x00, 0xFF, 0x66);      // #00FF66
        public static readonly SKColor SynthwaveMagenta = new(0xFF, 0x00, 0x7F); // #FF007F
        public static readonly SKColor SolarAmber = new(0xFF, 0xB0, 0x00);       // #FFB000
        public static readonly SKColor PhosphorWhite = new(0xF0, 0xF0, 0xF0);    // #F0F0F0
        public static readonly SKColor GlitchRed = new(0xFF, 0x22, 0x44);        // #FF2244
        public static readonly SKColor NightCityViolet = new(0xA0, 0x40, 0xFF);  // #A040FF

        // Dark Background & Scope Colors
        public static readonly SKColor RadarDarkBg = new(0x05, 0x08, 0x12, 0xFA);
        public static readonly SKColor RadarCenterBg = new(0x0A, 0x12, 0x24, 0xFA);
        public static readonly SKColor PureBlack = new(0x00, 0x00, 0x00);
        public static readonly SKColor PureWhite = new(0xFF, 0xFF, 0xFF);

        // Mask Filters for Glow and Bloom
        public static readonly SKMaskFilter SoftGlowFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 12.0f);
        public static readonly SKMaskFilter MediumGlowFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6.0f);
        public static readonly SKMaskFilter CrispGlowFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.5f);

        public static SKColor ParseHexColor(string hex, SKColor defaultColor)
        {
            if (string.IsNullOrWhiteSpace(hex)) return defaultColor;
            hex = hex.Trim().TrimStart('#');
            if (hex.Length == 6)
            {
                if (byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
                    byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
                    byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
                {
                    return new SKColor(r, g, b);
                }
            }
            else if (hex.Length == 8)
            {
                if (byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte a) &&
                    byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
                    byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
                    byte.TryParse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
                {
                    return new SKColor(r, g, b, a);
                }
            }
            return defaultColor;
        }

        public static SKColor GetRgbSpectrumColor(double totalSeconds, double speed)
        {
            double hue = (totalSeconds * 60.0 * speed) % 360.0;
            return HsvToRgb((float)hue, 0.95f, 1.0f);
        }

        public static SKColor HsvToRgb(float hue, float saturation, float value)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            float f = hue / 60 - (float)Math.Floor(hue / 60);

            value = value * 255;
            byte v = Convert.ToByte(value);
            byte p = Convert.ToByte(value * (1 - saturation));
            byte q = Convert.ToByte(value * (1 - f * saturation));
            byte t = Convert.ToByte(value * (1 - (1 - f) * saturation));

            return hi switch
            {
                0 => new SKColor(v, t, p),
                1 => new SKColor(q, v, p),
                2 => new SKColor(p, v, t),
                3 => new SKColor(p, q, v),
                4 => new SKColor(t, p, v),
                _ => new SKColor(v, p, q)
            };
        }
    }
}

