using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CyberPunkNetRadar.Models;
using CyberPunkNetRadar.Settings;
using SkiaSharp;

namespace CyberPunkNetRadar.Rendering
{
    public class RadarRenderer : IDisposable
    {
        private readonly SKTypeface _monoTypeface;
        private readonly SKTypeface _monoBoldTypeface;

        // Pre-allocated reusable cached paints for zero-allocation 60+ FPS rendering
        private readonly SKPaint _bgFillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint _ringPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
        private readonly SKPaint _ringLabelPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint _axisPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
        private readonly SKPaint _sweepLinePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
        private readonly SKPaint _sweepLineCorePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
        private readonly SKPaint _blipCorePaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint _blipGlowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill, MaskFilter = RadarPalette.MediumGlowFilter };
        private readonly SKPaint _blipLabelPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint _hudCardBgPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint _hudCardBorderPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
        private readonly SKPaint _hudTextPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint _reticlePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };

        private readonly SKPaint _rimGlowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4.0f, MaskFilter = RadarPalette.MediumGlowFilter };
        private readonly SKPaint _rimPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.0f };
        private readonly SKPaint _outerDashPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.0f, PathEffect = SKPathEffect.CreateDash([3.0f, 4.0f], 0) };
        private readonly SKPaint _diagPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.0f, PathEffect = SKPathEffect.CreateDash([2.0f, 6.0f], 0) };
        private readonly SKPaint _cardinalPaint = new() { IsAntialias = true, TextSize = 11.0f };
        private readonly SKPaint _tickPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.0f };
        private readonly SKPaint _fanPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint _beamBloomPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 6.0f, StrokeCap = SKStrokeCap.Round, MaskFilter = RadarPalette.MediumGlowFilter };
        private readonly SKPaint _hubBloomPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill, MaskFilter = RadarPalette.CrispGlowFilter };
        private readonly SKPaint _hubCorePaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.White };
        private readonly SKPaint _sparkPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint _leaderPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f };
        private readonly SKPaint _headerBgPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

        // Reusable reusable paths
        private readonly SKPath _segPath = new();
        private readonly SKPath _diamondPath = new();

        private float _previousSweepAngle = 0f;
        private bool _isFirstFrame = true;

        public RadarRenderer()
        {
            _monoTypeface = SKTypeface.FromFamilyName("Consolas") ?? SKTypeface.Default;
            _monoBoldTypeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold) ?? SKTypeface.Default;

            _ringLabelPaint.Typeface = _monoTypeface;
            _blipLabelPaint.Typeface = _monoTypeface;
            _hudTextPaint.Typeface = _monoTypeface;
            _cardinalPaint.Typeface = _monoBoldTypeface;
        }

        public void Render(
            SKCanvas canvas,
            int width,
            int height,
            float currentSweepAngle,
            IReadOnlyList<NetworkEndpointModel> endpoints,
            WidgetSettings settings,
            SKPoint? mouseHoverPos,
            double totalSeconds,
            out NetworkEndpointModel? hoveredEndpoint)
        {
            hoveredEndpoint = null;
            canvas.Clear(SKColors.Transparent);

            float cx = width / 2.0f;
            float cy = height / 2.0f;
            float margin = 28.0f;
            float radius = Math.Max(20.0f, Math.Min(cx, cy) - margin);

            // Determine active accent color
            SKColor accentColor = settings.RgbSpectrumCycle
                ? RadarPalette.GetRgbSpectrumColor(totalSeconds, settings.RgbCycleSpeed)
                : RadarPalette.ParseHexColor(settings.AccentColorHex, RadarPalette.ElectricCyan);

            float brightness = (float)Math.Clamp(settings.LedBrightness, 0.1, 1.0);

            // Determine active max range in ms
            double maxRangeMs = settings.MaxRangeMs;
            if (settings.IsAutoScaleRange || maxRangeMs <= 0)
            {
                double maxObserved = endpoints.Count > 0 ? endpoints.Max(e => e.LatencyMs) : 30.0;
                maxRangeMs = Math.Max(5.0, Math.Ceiling(maxObserved / 5.0) * 5.0);
            }

            // Update sweep illumination for endpoints
            UpdateSweepIllumination(endpoints, _previousSweepAngle, currentSweepAngle);
            _previousSweepAngle = currentSweepAngle;

            // 1. Draw Radar Dark Background Scope
            DrawRadarBackground(canvas, cx, cy, radius, accentColor);

            // 2. Draw Latency Rings (5ms, 10ms, 15ms, 20ms, 25ms, 30ms or scaled)
            DrawLatencyRings(canvas, cx, cy, radius, maxRangeMs, accentColor, brightness);

            // 3. Draw Compass Cardinal Marks & Degree Ticks
            DrawCompassAndAxes(canvas, cx, cy, radius, accentColor, brightness);

            // 4. Draw Trailing Sweep Cone & Sweep Ray Beam
            DrawSweepBeam(canvas, cx, cy, radius, currentSweepAngle, accentColor, brightness);

            // 5. Calculate Blip Screen Positions & Draw Blips
            DrawBlips(canvas, cx, cy, radius, maxRangeMs, endpoints, settings, accentColor, brightness, mouseHoverPos, out hoveredEndpoint);

            // 6. Draw On-Hover Cyber Targeting Reticle & Metadata HUD Card
            if (hoveredEndpoint != null && mouseHoverPos.HasValue)
            {
                DrawHoverTargetHud(canvas, cx, cy, radius, hoveredEndpoint, accentColor, totalSeconds);
            }

            // 7. Draw Digital Corner Telemetry
            if (settings.ShowDigitalTelemetry)
            {
                DrawDigitalTelemetry(canvas, width, height, currentSweepAngle, endpoints, settings, maxRangeMs, accentColor, brightness);
            }
        }

        private void DrawRadarBackground(SKCanvas canvas, float cx, float cy, float radius, SKColor accentColor)
        {
            // Radial gradient dark background fill
            SKColor innerBg = new SKColor((byte)(accentColor.Red * 0.08), (byte)(accentColor.Green * 0.08), (byte)(accentColor.Blue * 0.08), 245);
            SKColor outerBg = new SKColor(0x04, 0x07, 0x10, 250);

            using var bgShader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy),
                radius,
                [innerBg, outerBg],
                [0.0f, 1.0f],
                SKShaderTileMode.Clamp);

            _bgFillPaint.Shader = bgShader;
            canvas.DrawCircle(cx, cy, radius, _bgFillPaint);
            _bgFillPaint.Shader = null;

            // Outer Scope Rim Glow & Bezel
            _rimGlowPaint.Color = new SKColor(accentColor.Red, accentColor.Green, accentColor.Blue, 0x60);
            canvas.DrawCircle(cx, cy, radius, _rimGlowPaint);

            _rimPaint.Color = new SKColor(accentColor.Red, accentColor.Green, accentColor.Blue, 0xD0);
            canvas.DrawCircle(cx, cy, radius, _rimPaint);

            _outerDashPaint.Color = new SKColor(accentColor.Red, accentColor.Green, accentColor.Blue, 0x40);
            canvas.DrawCircle(cx, cy, radius + 8.0f, _outerDashPaint);
        }

        private void DrawLatencyRings(SKCanvas canvas, float cx, float cy, float radius, double maxRangeMs, SKColor accentColor, float brightness)
        {
            int ringSubdivisions = maxRangeMs switch
            {
                5.0 => 5,   // 1ms, 2ms, 3ms, 4ms, 5ms
                100.0 => 5, // 20ms, 40ms, 60ms, 80ms, 100ms
                _ => 6      // 30ms -> 5ms step, 60ms -> 10ms step, 300ms -> 50ms step
            };

            double stepMs = maxRangeMs / ringSubdivisions;

            byte ringAlpha = (byte)Math.Clamp(0x35 * brightness, 10, 255);
            _ringPaint.Color = new SKColor(accentColor.Red, accentColor.Green, accentColor.Blue, ringAlpha);
            _ringPaint.StrokeWidth = 1.0f;

            _ringLabelPaint.Color = new SKColor(accentColor.Red, accentColor.Green, accentColor.Blue, (byte)Math.Clamp(0x90 * brightness, 20, 255));
            _ringLabelPaint.TextSize = 10.0f;

            for (int i = 1; i <= ringSubdivisions; i++)
            {
                float ringR = radius * (i / (float)ringSubdivisions);
                double currentMs = stepMs * i;

                // Faint concentric circle
                canvas.DrawCircle(cx, cy, ringR, _ringPaint);

                // Small cyberpunk distance label along North vertical axis
                string label = (currentMs % 1.0 == 0) ? $"{currentMs:0}ms" : $"{currentMs:0.#}ms";
                float textW = _ringLabelPaint.MeasureText(label);
                canvas.DrawText(label, cx - textW / 2.0f, cy - ringR + 11.0f, _ringLabelPaint);
            }
        }

        private void DrawCompassAndAxes(SKCanvas canvas, float cx, float cy, float radius, SKColor accentColor, float brightness)
        {
            byte axisAlpha = (byte)Math.Clamp(0x28 * brightness, 8, 255);
            _axisPaint.Color = new SKColor(accentColor.Red, accentColor.Green, accentColor.Blue, axisAlpha);
            _axisPaint.StrokeWidth = 1.0f;

            // Crosshair vertical & horizontal axes
            canvas.DrawLine(cx - radius, cy, cx + radius, cy, _axisPaint);
            canvas.DrawLine(cx, cy - radius, cx, cy + radius, _axisPaint);

            // Diagonal reference ticks
            _diagPaint.Color = new SKColor(accentColor.Red, accentColor.Green, accentColor.Blue, (byte)(axisAlpha * 0.6));
            float cos45 = 0.7071f * radius;
            canvas.DrawLine(cx - cos45, cy - cos45, cx + cos45, cy + cos45, _diagPaint);
            canvas.DrawLine(cx - cos45, cy + cos45, cx + cos45, cy - cos45, _diagPaint);

            // Cardinal Heading Labels (N, E, S, W)
            _cardinalPaint.Color = new SKColor(accentColor.Red, accentColor.Green, accentColor.Blue, (byte)Math.Clamp(0xB0 * brightness, 30, 255));
            canvas.DrawText("N 000°", cx - 18.0f, cy - radius - 10.0f, _cardinalPaint);
            canvas.DrawText("S 180°", cx - 18.0f, cy + radius + 19.0f, _cardinalPaint);
            canvas.DrawText("E 090°", cx + radius + 10.0f, cy + 4.0f, _cardinalPaint);
            canvas.DrawText("W 270°", cx - radius - 48.0f, cy + 4.0f, _cardinalPaint);

            // Perimeter Angular Ticks every 15 degrees
            _tickPaint.Color = new SKColor(accentColor.Red, accentColor.Green, accentColor.Blue, (byte)Math.Clamp(0x70 * brightness, 15, 255));

            for (int deg = 0; deg < 360; deg += 15)
            {
                float rad = (deg - 90) * (float)Math.PI / 180.0f;
                float cos = (float)Math.Cos(rad);
                float sin = (float)Math.Sin(rad);

                float tickLen = (deg % 45 == 0) ? 7.0f : 4.0f;
                float x1 = cx + (radius - tickLen) * cos;
                float y1 = cy + (radius - tickLen) * sin;
                float x2 = cx + radius * cos;
                float y2 = cy + radius * sin;

                canvas.DrawLine(x1, y1, x2, y2, _tickPaint);
            }
        }

        private void DrawSweepBeam(SKCanvas canvas, float cx, float cy, float radius, float sweepAngle, SKColor accentColor, float brightness)
        {
            // 1. Draw Trailing Sweep Fan/Cone (fading gradient trailing 55 degrees behind the beam)
            float trailDegrees = 55.0f;
            int fanSegments = 24;

            for (int i = 0; i < fanSegments; i++)
            {
                float frac1 = i / (float)fanSegments;
                float frac2 = (i + 1) / (float)fanSegments;

                float a1 = sweepAngle - (1.0f - frac1) * trailDegrees;
                float a2 = sweepAngle - (1.0f - frac2) * trailDegrees;

                float rad1 = (a1 - 90) * (float)Math.PI / 180.0f;
                float rad2 = (a2 - 90) * (float)Math.PI / 180.0f;

                float x1 = cx + radius * (float)Math.Cos(rad1);
                float y1 = cy + radius * (float)Math.Sin(rad1);
                float x2 = cx + radius * (float)Math.Cos(rad2);
                float y2 = cy + radius * (float)Math.Sin(rad2);

                float alphaFrac = frac2 * frac2;
                byte segAlpha = (byte)Math.Clamp(alphaFrac * 0x48 * brightness, 0, 255);
                _fanPaint.Color = new SKColor(accentColor.Red, accentColor.Green, accentColor.Blue, segAlpha);

                _segPath.Rewind();
                _segPath.MoveTo(cx, cy);
                _segPath.LineTo(x1, y1);
                _segPath.LineTo(x2, y2);
                _segPath.Close();

                canvas.DrawPath(_segPath, _fanPaint);
            }

            // 2. Draw Leading Beam Ray with White-Hot Core & Neon Bloom
            float sweepRad = (sweepAngle - 90) * (float)Math.PI / 180.0f;
            float endX = cx + radius * (float)Math.Cos(sweepRad);
            float endY = cy + radius * (float)Math.Sin(sweepRad);

            // Wide Beam Bloom
            _beamBloomPaint.Color = new SKColor(accentColor.Red, accentColor.Green, accentColor.Blue, (byte)Math.Clamp(0x80 * brightness, 20, 255));
            canvas.DrawLine(cx, cy, endX, endY, _beamBloomPaint);

            // Crisp Accent Ray
            _sweepLinePaint.Color = new SKColor(accentColor.Red, accentColor.Green, accentColor.Blue, (byte)Math.Clamp(0xE0 * brightness, 50, 255));
            _sweepLinePaint.StrokeWidth = 2.0f;
            canvas.DrawLine(cx, cy, endX, endY, _sweepLinePaint);

            // Hot White Core Center Line
            _sweepLineCorePaint.Color = new SKColor(0xFF, 0xFF, 0xFF, (byte)Math.Clamp(0xF0 * brightness, 80, 255));
            _sweepLineCorePaint.StrokeWidth = 1.0f;
            canvas.DrawLine(cx, cy, endX, endY, _sweepLineCorePaint);

            // Center Pulse Hub
            _hubBloomPaint.Color = new SKColor(accentColor.Red, accentColor.Green, accentColor.Blue, 0x90);
            canvas.DrawCircle(cx, cy, 6.0f, _hubBloomPaint);
            canvas.DrawCircle(cx, cy, 2.5f, _hubCorePaint);
        }

        private void DrawBlips(
            SKCanvas canvas,
            float cx,
            float cy,
            float radius,
            double maxRangeMs,
            IReadOnlyList<NetworkEndpointModel> endpoints,
            WidgetSettings settings,
            SKColor accentColor,
            float brightness,
            SKPoint? mouseHoverPos,
            out NetworkEndpointModel? hoveredEndpoint)
        {
            hoveredEndpoint = null;
            float closestDistSq = float.MaxValue;
            float hoverThresholdSq = 18.0f * 18.0f;

            double fadeDuration = settings.BlipFadeDurationSeconds;

            foreach (var ep in endpoints)
            {
                // Calculate distance from latency
                double normDist = Math.Clamp(ep.LatencyMs / maxRangeMs, 0.05, 0.98);
                float blipR = (float)(normDist * radius);

                float blipAngle = ep.BearingDeg;
                float blipRad = (blipAngle - 90) * (float)Math.PI / 180.0f;

                float bx = cx + blipR * (float)Math.Cos(blipRad);
                float by = cy + blipR * (float)Math.Sin(blipRad);

                ep.ScreenX = bx;
                ep.ScreenY = by;
                ep.NormalizedRadius = (float)normDist;

                // Check for mouse hover proximity
                if (mouseHoverPos.HasValue)
                {
                    float dx = mouseHoverPos.Value.X - bx;
                    float dy = mouseHoverPos.Value.Y - by;
                    float distSq = dx * dx + dy * dy;
                    if (distSq < hoverThresholdSq && distSq < closestDistSq)
                    {
                        closestDistSq = distSq;
                        hoveredEndpoint = ep;
                    }
                }

                // Phosphor intensity calculation (1.0 down to 0.10)
                float intensity = ep.GetPhosphorIntensity(fadeDuration) * brightness;

                // Blip Color: Private LAN = Soft Amber, External WAN = Theme Accent Color, Fallback = Magenta Tint
                SKColor blipBaseColor = ep.IsPrivateLan
                    ? RadarPalette.SolarAmber
                    : (ep.IsFallbackPing ? new SKColor(accentColor.Red, (byte)(accentColor.Green * 0.8), 0xFF) : accentColor);

                byte glowAlpha = (byte)Math.Clamp(intensity * 200, 10, 255);
                byte coreAlpha = (byte)Math.Clamp(intensity * 255, 30, 255);

                // 1. Phosphor Bloom Glow
                _blipGlowPaint.Color = new SKColor(blipBaseColor.Red, blipBaseColor.Green, blipBaseColor.Blue, glowAlpha);
                float glowSize = 4.0f + intensity * 6.0f;
                canvas.DrawCircle(bx, by, glowSize, _blipGlowPaint);

                // 2. Crisp Blip Core Shape (TCP = Circle, UDP = Diamond)
                _blipCorePaint.Color = new SKColor(blipBaseColor.Red, blipBaseColor.Green, blipBaseColor.Blue, coreAlpha);

                if (ep.Protocol.StartsWith("UDP"))
                {
                    float s = 3.0f + intensity * 2.0f;
                    _diamondPath.Rewind();
                    _diamondPath.MoveTo(bx, by - s);
                    _diamondPath.LineTo(bx + s, by);
                    _diamondPath.LineTo(bx, by + s);
                    _diamondPath.LineTo(bx - s, by);
                    _diamondPath.Close();
                    canvas.DrawPath(_diamondPath, _blipCorePaint);
                }
                else
                {
                    float r = 2.5f + intensity * 2.5f;
                    canvas.DrawCircle(bx, by, r, _blipCorePaint);
                }

                // 3. Hot White Center Spark on high intensity (freshly swept)
                if (intensity > 0.6f)
                {
                    _sparkPaint.Color = new SKColor(0xFF, 0xFF, 0xFF, (byte)(intensity * 240));
                    canvas.DrawCircle(bx, by, 1.5f, _sparkPaint);
                }

                // 4. Blip Label (e.g. process or IP) if enabled
                if (settings.ShowBlipLabels && intensity > 0.35f)
                {
                    string label = $"{ep.ProcessName} ({ep.LatencyMs:0.#}ms)";
                    _blipLabelPaint.TextSize = 9.0f;
                    _blipLabelPaint.Color = new SKColor(0xDD, 0xEE, 0xFF, (byte)(intensity * 180));
                    canvas.DrawText(label, bx + 7.0f, by + 3.0f, _blipLabelPaint);
                }
            }
        }

        private void DrawHoverTargetHud(
            SKCanvas canvas,
            float cx,
            float cy,
            float radius,
            NetworkEndpointModel ep,
            SKColor accentColor,
            double totalSeconds)
        {
            float bx = ep.ScreenX;
            float by = ep.ScreenY;

            // 1. Rotating Cyber Lock-On Reticle around blip
            float rotAngle = (float)(totalSeconds * 90.0) % 360.0f;
            float reticleR = 12.0f;

            _reticlePaint.Color = new SKColor(0xFF, 0x22, 0x55, 0xE0); // Neon Glitch Red Reticle
            _reticlePaint.StrokeWidth = 1.5f;

            canvas.Save();
            canvas.Translate(bx, by);
            canvas.RotateDegrees(rotAngle);

            // 4 Corner Brackets
            float bSize = 4.0f;
            canvas.DrawLine(-reticleR, -reticleR + bSize, -reticleR, -reticleR, _reticlePaint);
            canvas.DrawLine(-reticleR, -reticleR, -reticleR + bSize, -reticleR, _reticlePaint);
            canvas.DrawLine(reticleR, -reticleR + bSize, reticleR, -reticleR, _reticlePaint);
            canvas.DrawLine(reticleR, -reticleR, reticleR - bSize, -reticleR, _reticlePaint);
            canvas.DrawLine(-reticleR, reticleR - bSize, -reticleR, reticleR, _reticlePaint);
            canvas.DrawLine(-reticleR, reticleR, -reticleR + bSize, reticleR, _reticlePaint);
            canvas.DrawLine(reticleR, reticleR - bSize, reticleR, reticleR, _reticlePaint);
            canvas.DrawLine(reticleR, reticleR, reticleR - bSize, reticleR, _reticlePaint);

            canvas.Restore();

            // 2. HUD Info Leader Line
            float cardW = 190.0f;
            float cardH = 88.0f;

            float cardX = bx > cx ? bx - cardW - 25.0f : bx + 25.0f;
            float cardY = by > cy ? by - cardH - 15.0f : by + 15.0f;

            _leaderPaint.Color = new SKColor(accentColor.Red, accentColor.Green, accentColor.Blue, 0xB0);
            float elbowX = bx > cx ? bx - 14.0f : bx + 14.0f;
            float elbowY = by > cy ? by - 14.0f : by + 14.0f;

            canvas.DrawLine(bx, by, elbowX, elbowY, _leaderPaint);
            canvas.DrawLine(elbowX, elbowY, bx > cx ? cardX + cardW : cardX, cardY + 12.0f, _leaderPaint);

            // 3. Futuristic HUD Metadata Card
            _hudCardBgPaint.Color = new SKColor(0x06, 0x0A, 0x16, 0xF2);
            _hudCardBorderPaint.Color = new SKColor(accentColor.Red, accentColor.Green, accentColor.Blue, 0xD0);
            _hudCardBorderPaint.StrokeWidth = 1.0f;

            var cardRect = new SKRoundRect(new SKRect(cardX, cardY, cardX + cardW, cardY + cardH), 4.0f);
            canvas.DrawRoundRect(cardRect, _hudCardBgPaint);
            canvas.DrawRoundRect(cardRect, _hudCardBorderPaint);

            _headerBgPaint.Color = new SKColor(accentColor.Red, accentColor.Green, accentColor.Blue, 0x30);
            canvas.DrawRect(cardX, cardY, cardW, 18.0f, _headerBgPaint);

            // Text Content
            _hudTextPaint.TextSize = 10.0f;
            _hudTextPaint.Typeface = _monoBoldTypeface;
            _hudTextPaint.Color = accentColor;
            canvas.DrawText("TARGET LOCKED // " + ep.Protocol, cardX + 8.0f, cardY + 13.0f, _hudTextPaint);

            _hudTextPaint.Typeface = _monoTypeface;
            _hudTextPaint.TextSize = 9.5f;
            _hudTextPaint.Color = SKColors.White;

            canvas.DrawText($"IP: {ep.RemoteIp}:{ep.RemotePort}", cardX + 8.0f, cardY + 32.0f, _hudTextPaint);
            canvas.DrawText($"PROCESS: {ep.ProcessName} [PID {ep.ProcessId}]", cardX + 8.0f, cardY + 46.0f, _hudTextPaint);

            string pingType = ep.IsFallbackPing ? "(EST)" : "(ICMP)";
            _hudTextPaint.Color = ep.LatencyMs < 20.0 ? RadarPalette.MatrixGreen : RadarPalette.SolarAmber;
            canvas.DrawText($"LATENCY: {ep.LatencyMs:0.0} ms {pingType}", cardX + 8.0f, cardY + 60.0f, _hudTextPaint);

            _hudTextPaint.Color = new SKColor(0x90, 0xA0, 0xC0);
            canvas.DrawText($"BEARING: {ep.BearingDeg:000}° | {(ep.IsPrivateLan ? "LAN" : "WAN")}", cardX + 8.0f, cardY + 74.0f, _hudTextPaint);
        }

        private void DrawDigitalTelemetry(
            SKCanvas canvas,
            int width,
            int height,
            float sweepAngle,
            IReadOnlyList<NetworkEndpointModel> endpoints,
            WidgetSettings settings,
            double maxRangeMs,
            SKColor accentColor,
            float brightness)
        {
            _hudTextPaint.Typeface = _monoTypeface;
            _hudTextPaint.TextSize = 10.0f;
            _hudTextPaint.Color = new SKColor(accentColor.Red, accentColor.Green, accentColor.Blue, (byte)Math.Clamp(0xA0 * brightness, 30, 255));

            int total = endpoints.Count;
            int tcp = endpoints.Count(e => e.Protocol.StartsWith("TCP"));
            int udp = total - tcp;

            double avgPing = total > 0 ? endpoints.Average(e => e.LatencyMs) : 0.0;
            double minPing = total > 0 ? endpoints.Min(e => e.LatencyMs) : 0.0;

            // Top-Left Telemetry
            canvas.DrawText("NET.RADAR // ONLINE", 12.0f, 18.0f, _hudTextPaint);
            canvas.DrawText($"TARGETS: {total} [TCP:{tcp} UDP:{udp}]", 12.0f, 32.0f, _hudTextPaint);

            // Top-Right Telemetry
            string sweepInfo = $"SWEEP: {settings.SweepDurationSeconds:0.#}s | {sweepAngle:000}°";
            float sweepW = _hudTextPaint.MeasureText(sweepInfo);
            canvas.DrawText(sweepInfo, width - sweepW - 12.0f, 18.0f, _hudTextPaint);

            string rangeInfo = settings.IsAutoScaleRange ? $"RANGE: AUTO ({maxRangeMs:0}ms)" : $"RANGE: {maxRangeMs:0}ms";
            float rangeW = _hudTextPaint.MeasureText(rangeInfo);
            canvas.DrawText(rangeInfo, width - rangeW - 12.0f, 32.0f, _hudTextPaint);

            // Bottom-Left Telemetry
            canvas.DrawText($"PING: AVG {avgPing:0.#}ms | MIN {minPing:0.#}ms", 12.0f, height - 12.0f, _hudTextPaint);

            // Bottom-Right Telemetry
            string decayInfo = $"DECAY: {settings.BlipFadeDurationSeconds:0.#}s";
            float decayW = _hudTextPaint.MeasureText(decayInfo);
            canvas.DrawText(decayInfo, width - decayW - 12.0f, height - 12.0f, _hudTextPaint);
        }

        private void UpdateSweepIllumination(IReadOnlyList<NetworkEndpointModel> endpoints, float prevAngle, float currAngle)
        {
            if (_isFirstFrame)
            {
                _isFirstFrame = false;
                DateTime now = DateTime.UtcNow;
                foreach (var ep in endpoints)
                {
                    ep.LastIlluminatedTime = now;
                    ep.HasBeenSwept = true;
                }
                return;
            }

            DateTime sweepTime = DateTime.UtcNow;

            foreach (var ep in endpoints)
            {
                float b = ep.BearingDeg;
                bool passed = false;

                if (currAngle >= prevAngle)
                {
                    if (b >= prevAngle && b <= currAngle) passed = true;
                }
                else
                {
                    if (b >= prevAngle || b <= currAngle) passed = true;
                }

                if (passed)
                {
                    ep.LastIlluminatedTime = sweepTime;
                    ep.HasBeenSwept = true;
                }
            }
        }

        public void Dispose()
        {
            _bgFillPaint.Dispose();
            _ringPaint.Dispose();
            _ringLabelPaint.Dispose();
            _axisPaint.Dispose();
            _sweepLinePaint.Dispose();
            _sweepLineCorePaint.Dispose();
            _blipCorePaint.Dispose();
            _blipGlowPaint.Dispose();
            _blipLabelPaint.Dispose();
            _hudCardBgPaint.Dispose();
            _hudCardBorderPaint.Dispose();
            _hudTextPaint.Dispose();
            _reticlePaint.Dispose();
            _rimGlowPaint.Dispose();
            _rimPaint.Dispose();
            _outerDashPaint.Dispose();
            _diagPaint.Dispose();
            _cardinalPaint.Dispose();
            _tickPaint.Dispose();
            _fanPaint.Dispose();
            _beamBloomPaint.Dispose();
            _hubBloomPaint.Dispose();
            _hubCorePaint.Dispose();
            _sparkPaint.Dispose();
            _leaderPaint.Dispose();
            _headerBgPaint.Dispose();
            _segPath.Dispose();
            _diamondPath.Dispose();
            _monoTypeface.Dispose();
            _monoBoldTypeface.Dispose();
        }
    }
}

