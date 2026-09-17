using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using CyberPunkNetRadar.Models;
using CyberPunkNetRadar.Rendering;
using CyberPunkNetRadar.Services;
using CyberPunkNetRadar.Settings;
using CyberPunkNetRadar.UI;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace CyberPunkNetRadar
{
    public partial class MainWindow : Window
    {
        private static readonly Random _random = new Random();

        // Cached pre-frozen brushes for CRT glitches
        private static readonly Brush[] GlitchBrushes = CreateGlitchBrushes();

        // Shared static retro CRT static noise frames (256x256 frozen WriteableBitmaps)
        private static readonly List<WriteableBitmap> SharedSnowBitmaps = new List<WriteableBitmap>();
        private static readonly object SnowLock = new object();

        // Pre-frozen rainbow rotation animation
        private static readonly DoubleAnimation RainbowAnimation = CreateRainbowAnimation();

        // Services & Engine
        private readonly WidgetSettings _settings;
        private readonly NetworkMonitorService _networkService;
        private readonly RadarRenderer _renderer;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        // State variables
        private bool _isInitialized = false;
        private bool _isClosed = false;
        private float _currentSweepAngle = 0f;
        private SKPoint? _mouseHoverPos = null;
        private NetworkEndpointModel? _hoveredEndpoint = null;

        // Visual FX Timers
        private readonly DispatcherTimer _glitchTimer;
        private int _glitchCooldown = 0;

        private readonly DispatcherTimer _snowTimer;
        private int _snowFrameIndex = 0;

        private readonly DispatcherTimer _effectsCloseTimer;
        private FrameworkElement? _subscribedPopupChild = null;

        public MainWindow()
        {
            _settings = WidgetSettings.Load();
            _networkService = new NetworkMonitorService();
            _renderer = new RadarRenderer();

            // Set up CRT glitch timer (120ms tick rate)
            _glitchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _glitchTimer.Tick += GlitchTimer_Tick;

            // Set up CRT snow static animation timer (65ms tick rate)
            _snowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(65) };
            _snowTimer.Tick += SnowTimer_Tick;

            // Set up Effects submenu close delay timer (700ms)
            _effectsCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _effectsCloseTimer.Tick += EffectsCloseTimer_Tick;

            App.EnsureStandardMenuDropAlignment();

            InitializeComponent();

            _isInitialized = true;

            Loaded += (s, e) => UpdateCircularGeometry();

            // Restore Window Position & Size
            ApplySavedSettings();

            // Hook CompositionTarget.Rendering for smooth 60 FPS SkiaSharp animation
            CompositionTarget.Rendering += OnCompositionRendering;

            // Handle display resolution / monitor changes
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        #region Setup & Configuration

        private void ApplySavedSettings()
        {
            if (_settings.WindowWidth.HasValue && _settings.WindowWidth.Value >= MinWidth)
                Width = _settings.WindowWidth.Value;
            if (_settings.WindowHeight.HasValue && _settings.WindowHeight.Value >= MinHeight)
                Height = _settings.WindowHeight.Value;

            if (_settings.WindowLeft.HasValue && _settings.WindowTop.HasValue)
            {
                Left = _settings.WindowLeft.Value;
                Top = _settings.WindowTop.Value;
                EnsureWindowVisibleOnScreen();
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            Topmost = _settings.AlwaysOnTop;
            AlwaysOnTopMenuItem.IsChecked = _settings.AlwaysOnTop;

            if (RadarDropShadow != null)
                RadarDropShadow.Opacity = _settings.WindowShadow ? 0.40 : 0.0;
            ShadowMenuItem.IsChecked = _settings.WindowShadow;

            // Opacity
            MainRadarContainer.Opacity = _settings.WindowGlassOpacity;
            GlassOpacitySlider.Value = _settings.WindowGlassOpacity;
            GlassOpacityValueText.Text = $"{Math.Round(_settings.WindowGlassOpacity * 100)}%";

            LedBrightnessSlider.Value = _settings.LedBrightness;
            LedBrightnessValueText.Text = $"{Math.Round(_settings.LedBrightness * 100)}%";

            // Sweep Speed
            SweepSpeedSlider.Value = _settings.SweepDurationSeconds;
            SweepSpeedValueText.Text = $"{_settings.SweepDurationSeconds:0.#}s";
            UpdateSweepPresetCheckmarks(_settings.SweepDurationSeconds);

            // Range Mode
            UpdateRangeModeCheckmarks();

            // Fade Duration
            FadeDurationSlider.Value = _settings.BlipFadeDurationSeconds;
            FadeDurationValueText.Text = $"{_settings.BlipFadeDurationSeconds:0.#}s";

            // RGB Cycle
            RgbCycleMenuItem.IsChecked = _settings.RgbSpectrumCycle;
            RgbCycleSpeedSlider.Value = _settings.RgbCycleSpeed;
            RgbCycleSpeedValueText.Text = $"{Math.Round(_settings.RgbCycleSpeed, 1)}x";

            // Color Palette
            UpdateColorMenuCheckmarks(_settings.AccentColorHex);

            // Labels & Telemetry
            ShowLabelsMenuItem.IsChecked = _settings.ShowBlipLabels;
            ShowTelemetryMenuItem.IsChecked = _settings.ShowDigitalTelemetry;

            // FX Suite: Rainbow Border
            RainbowBorderMenuItem.IsChecked = _settings.RainbowBorderEnabled;
            BorderWidthSlider.Value = _settings.BorderWidth;
            BorderWidthValueText.Text = $"{Math.Round(_settings.BorderWidth)}px";
            UpdateRainbowBorderState();

            // FX Suite: Scanlines
            CrtScanlinesMenuItem.IsChecked = _settings.CrtScanlinesEnabled;
            ScanlineThicknessSlider.Value = _settings.ScanlineThickness;
            ScanlineThicknessValueText.Text = $"{Math.Round(_settings.ScanlineThickness)}px";
            UpdateScanlinesState();

            // FX Suite: Glitch
            CrtGlitchMenuItem.IsChecked = _settings.CrtGlitchEnabled;
            GlitchChanceSlider.Value = _settings.GlitchChance;
            GlitchChanceValueText.Text = $"{Math.Round(_settings.GlitchChance)}%";
            UpdateGlitchState();

            // FX Suite: Snow
            CrtSnowMenuItem.IsChecked = _settings.CrtSnowEnabled;
            SnowAmountSlider.Value = _settings.SnowAmount;
            SnowAmountValueText.Text = $"{Math.Round(_settings.SnowAmount)}%";
            UpdateSnowState();
        }

        private void SaveCurrentSettings()
        {
            if (!_isInitialized || _isClosed) return;

            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
            _settings.Save();
        }

        private void EnsureWindowVisibleOnScreen()
        {
            double virtualLeft = SystemParameters.VirtualScreenLeft;
            double virtualTop = SystemParameters.VirtualScreenTop;
            double virtualWidth = SystemParameters.VirtualScreenWidth;
            double virtualHeight = SystemParameters.VirtualScreenHeight;

            if (Left + Width < virtualLeft + 40 || Left > virtualLeft + virtualWidth - 40 ||
                Top + Height < virtualTop + 40 || Top > virtualTop + virtualHeight - 40)
            {
                Left = (virtualWidth - Width) / 2 + virtualLeft;
                Top = (virtualHeight - Height) / 2 + virtualTop;
            }
        }

        #endregion

        #region 60 FPS Render Loop & SkiaSharp

        private void OnCompositionRendering(object? sender, EventArgs e)
        {
            if (!_isInitialized || _isClosed) return;

            double elapsedSec = _stopwatch.Elapsed.TotalSeconds;
            double sweepDuration = Math.Max(1.0, _settings.SweepDurationSeconds);

            // Compute current sweep angle (0 to 360 deg)
            _currentSweepAngle = (float)((elapsedSec / sweepDuration * 360.0) % 360.0);

            // Redraw radar canvas
            SkiaRadarElement.InvalidateVisual();
        }

        private void SkiaRadarElement_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;

            var endpoints = _networkService.GetActiveEndpointsSnapshot();
            double totalSec = _stopwatch.Elapsed.TotalSeconds;

            _renderer.Render(
                canvas,
                info.Width,
                info.Height,
                _currentSweepAngle,
                endpoints,
                _settings,
                _mouseHoverPos,
                totalSec,
                out _hoveredEndpoint);
        }

        private void SkiaRadarElement_MouseMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(SkiaRadarElement);
            // Convert WPF DPI-independent coordinates to Skia canvas pixel coordinates
            var presentationSource = PresentationSource.FromVisual(this);
            double dpiScaleX = presentationSource?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiScaleY = presentationSource?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            _mouseHoverPos = new SKPoint((float)(p.X * dpiScaleX), (float)(p.Y * dpiScaleY));
        }

        private void SkiaRadarElement_MouseLeave(object sender, MouseEventArgs e)
        {
            _mouseHoverPos = null;
            _hoveredEndpoint = null;
        }

        #endregion

        #region Visual Effects Suite (Rainbow, Scanlines, Glitch, Snow)

        private static DoubleAnimation CreateRainbowAnimation()
        {
            var anim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = new Duration(TimeSpan.FromSeconds(5)),
                RepeatBehavior = RepeatBehavior.Forever
            };
            anim.Freeze();
            return anim;
        }

        private static Brush[] CreateGlitchBrushes()
        {
            Color[] colors =
            {
                Color.FromArgb(220, 0, 240, 255),   // Neon cyan
                Color.FromArgb(220, 255, 0, 127),   // Neon magenta
                Color.FromArgb(240, 255, 255, 255), // Phosphor white
                Color.FromArgb(235, 0, 0, 0),       // Blackout dropout
                Color.FromArgb(210, 0, 255, 102),   // Neon lime
                Color.FromArgb(200, 255, 230, 0)    // Cyber yellow
            };

            var brushes = new Brush[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                var brush = new SolidColorBrush(colors[i]);
                brush.Freeze();
                brushes[i] = brush;
            }
            return brushes;
        }

        private static List<WriteableBitmap> GetOrCreateSnowBitmaps()
        {
            if (SharedSnowBitmaps.Count == 8) return SharedSnowBitmaps;

            lock (SnowLock)
            {
                if (SharedSnowBitmaps.Count == 8) return SharedSnowBitmaps;

                int w = 256, h = 256;
                for (int f = 0; f < 8; f++)
                {
                    var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
                    int stride = w * 4;
                    byte[] pixels = new byte[stride * h];
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        byte val = (byte)_random.Next(256);
                        bool colorSpeck = _random.Next(30) == 0;
                        pixels[i] = colorSpeck ? (byte)_random.Next(256) : val;     // B
                        pixels[i + 1] = colorSpeck ? (byte)_random.Next(256) : val; // G
                        pixels[i + 2] = colorSpeck ? (byte)_random.Next(256) : val; // R
                        pixels[i + 3] = 255;
                    }
                    wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                    wb.Freeze();
                    SharedSnowBitmaps.Add(wb);
                }
                return SharedSnowBitmaps;
            }
        }

        private void UpdateCircularGeometry()
        {
            if (ClippedRadarGrid == null) return;

            double w = ClippedRadarGrid.ActualWidth;
            double h = ClippedRadarGrid.ActualHeight;
            if (w <= 0 || h <= 0)
            {
                w = Width;
                h = Height;
            }
            if (w <= 0 || h <= 0) return;

            double cx = w / 2.0;
            double cy = h / 2.0;
            double margin = 28.0;
            double radius = Math.Max(20.0, Math.Min(cx, cy) - margin);

            // Update CRT circular clip
            if (CrtCircularClip != null)
            {
                CrtCircularClip.Center = new Point(cx, cy);
                CrtCircularClip.RadiusX = radius;
                CrtCircularClip.RadiusY = radius;
            }

            // Update Circular Rainbow Border size
            if (CyberpunkRainbowBorder != null)
            {
                double diameter = (radius * 2.0) + _settings.BorderWidth;
                CyberpunkRainbowBorder.Width = diameter;
                CyberpunkRainbowBorder.Height = diameter;
                CyberpunkRainbowBorder.StrokeThickness = _settings.BorderWidth;
            }
        }

        private void UpdateRainbowBorderState()
        {
            if (CyberpunkRainbowBorder == null) return;

            UpdateCircularGeometry();

            if (_settings.RainbowBorderEnabled)
            {
                CyberpunkRainbowBorder.StrokeThickness = _settings.BorderWidth;
                CyberpunkRainbowBorder.Visibility = Visibility.Visible;
                RainbowRotateTransform.BeginAnimation(RotateTransform.AngleProperty, RainbowAnimation);
            }
            else
            {
                CyberpunkRainbowBorder.Visibility = Visibility.Collapsed;
                RainbowRotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
            }
        }

        private void UpdateScanlinesState()
        {
            if (CrtScanlinesOverlay == null) return;

            if (_settings.CrtScanlinesEnabled)
            {
                UpdateScanlinesBrush(_settings.ScanlineThickness);
                CrtScanlinesOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                CrtScanlinesOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateScanlinesBrush(double thickness)
        {
            if (CrtDrawingBrush == null) return;

            double pitch = Math.Max(2.0, thickness * 2.0);
            CrtDrawingBrush.Viewport = new Rect(0, 0, 1, pitch);

            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 1, pitch))));
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)), null, new RectangleGeometry(new Rect(0, 0, 1, thickness))));
            group.Freeze();

            CrtDrawingBrush.Drawing = group;
        }

        private void UpdateGlitchState()
        {
            if (_settings.CrtGlitchEnabled)
            {
                if (!_glitchTimer.IsEnabled) _glitchTimer.Start();
            }
            else
            {
                _glitchTimer.Stop();
                if (CrtGlitchCanvas != null)
                {
                    CrtGlitchCanvas.Children.Clear();
                    CrtGlitchCanvas.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void GlitchTimer_Tick(object? sender, EventArgs e)
        {
            if (!_settings.CrtGlitchEnabled || CrtGlitchCanvas == null) return;

            if (_glitchCooldown > 0)
            {
                _glitchCooldown--;
                if (_glitchCooldown == 0)
                {
                    CrtGlitchCanvas.Children.Clear();
                    CrtGlitchCanvas.Visibility = Visibility.Collapsed;
                }
                return;
            }

            if (_random.NextDouble() * 100.0 < _settings.GlitchChance)
            {
                CrtGlitchCanvas.Children.Clear();
                CrtGlitchCanvas.Visibility = Visibility.Visible;

                double w = ClippedRadarGrid.ActualWidth;
                double h = ClippedRadarGrid.ActualHeight;
                if (w <= 0 || h <= 0) return;

                int sliceCount = _random.Next(2, 5);
                for (int i = 0; i < sliceCount; i++)
                {
                    double sliceY = _random.NextDouble() * h;
                    double sliceH = _random.Next(2, 10);
                    double sliceW = w * (0.3 + _random.NextDouble() * 0.7);
                    double sliceX = _random.NextDouble() * (w - sliceW);

                    var rect = new Rectangle
                    {
                        Width = sliceW,
                        Height = sliceH,
                        Fill = GlitchBrushes[_random.Next(GlitchBrushes.Length)],
                        Opacity = 0.75 + _random.NextDouble() * 0.25
                    };

                    Canvas.SetLeft(rect, sliceX);
                    Canvas.SetTop(rect, sliceY);
                    CrtGlitchCanvas.Children.Add(rect);
                }

                _glitchCooldown = _random.Next(1, 3);
            }
        }

        private void UpdateSnowState()
        {
            if (CrtSnowOverlay == null) return;

            if (_settings.CrtSnowEnabled)
            {
                CrtSnowOverlay.Visibility = Visibility.Visible;
                CrtSnowOverlay.Opacity = (_settings.SnowAmount / 100.0) * 0.35;
                if (!_snowTimer.IsEnabled) _snowTimer.Start();
            }
            else
            {
                _snowTimer.Stop();
                CrtSnowOverlay.Visibility = Visibility.Collapsed;
                CrtSnowOverlay.Opacity = 0.0;
            }
        }

        private void SnowTimer_Tick(object? sender, EventArgs e)
        {
            if (!_settings.CrtSnowEnabled || CrtSnowOverlay == null) return;

            var bitmaps = GetOrCreateSnowBitmaps();
            if (bitmaps.Count > 0)
            {
                _snowFrameIndex = (_snowFrameIndex + 1) % bitmaps.Count;
                CrtSnowOverlay.Source = bitmaps[_snowFrameIndex];
            }
        }

        #endregion

        #region Context Menu & Event Handlers

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
                SaveCurrentSettings();
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateCircularGeometry();
            SaveCurrentSettings();
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (_isClosed) return;
            EnsureWindowVisibleOnScreen();
            UpdateCircularGeometry();
        }

        private void Window_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            App.EnsureStandardMenuDropAlignment();
        }

        private void SweepSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _settings.SweepDurationSeconds = e.NewValue;
            if (SweepSpeedValueText != null)
                SweepSpeedValueText.Text = $"{e.NewValue:0.#}s";
            UpdateSweepPresetCheckmarks(e.NewValue);
            SaveCurrentSettings();
        }

        private void SweepPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is string tagStr && double.TryParse(tagStr, out double sec))
            {
                _settings.SweepDurationSeconds = sec;
                SweepSpeedSlider.Value = sec;
                if (SweepSpeedValueText != null)
                    SweepSpeedValueText.Text = $"{sec:0.#}s";
                UpdateSweepPresetCheckmarks(sec);
                SaveCurrentSettings();
            }
        }

        private void UpdateSweepPresetCheckmarks(double currentSec)
        {
            // Update preset checkmarks if in menu
        }

        private void RangeMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is string tagStr && double.TryParse(tagStr, out double range))
            {
                if (range == 0)
                {
                    _settings.IsAutoScaleRange = true;
                    _settings.MaxRangeMs = 30;
                }
                else
                {
                    _settings.IsAutoScaleRange = false;
                    _settings.MaxRangeMs = range;
                }
                UpdateRangeModeCheckmarks();
                SaveCurrentSettings();
            }
        }

        private void UpdateRangeModeCheckmarks()
        {
            if (RangeAutoMenuItem != null) RangeAutoMenuItem.IsChecked = _settings.IsAutoScaleRange;
            if (Range5MenuItem != null) Range5MenuItem.IsChecked = !_settings.IsAutoScaleRange && _settings.MaxRangeMs == 5;
            if (Range30MenuItem != null) Range30MenuItem.IsChecked = !_settings.IsAutoScaleRange && _settings.MaxRangeMs == 30;
            if (Range60MenuItem != null) Range60MenuItem.IsChecked = !_settings.IsAutoScaleRange && _settings.MaxRangeMs == 60;
            if (Range100MenuItem != null) Range100MenuItem.IsChecked = !_settings.IsAutoScaleRange && _settings.MaxRangeMs == 100;
            if (Range300MenuItem != null) Range300MenuItem.IsChecked = !_settings.IsAutoScaleRange && _settings.MaxRangeMs == 300;
        }

        private void FadeDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _settings.BlipFadeDurationSeconds = e.NewValue;
            if (FadeDurationValueText != null)
                FadeDurationValueText.Text = $"{e.NewValue:0.#}s";
            SaveCurrentSettings();
        }

        private void AccentColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is string hex)
            {
                _settings.RgbSpectrumCycle = false;
                RgbCycleMenuItem.IsChecked = false;
                _settings.AccentColorHex = hex;
                UpdateColorMenuCheckmarks(hex);
                SaveCurrentSettings();
            }
        }

        private void CustomColor_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ColorPickerWindow(_settings.AccentColorHex)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                _settings.RgbSpectrumCycle = false;
                RgbCycleMenuItem.IsChecked = false;
                _settings.AccentColorHex = dialog.SelectedHexColor;
                ClearColorMenuCheckmarks();
                SaveCurrentSettings();
            }
        }

        private void UpdateColorMenuCheckmarks(string currentHex)
        {
            if (AccentColorMenuItem == null) return;
            foreach (var item in AccentColorMenuItem.Items)
            {
                if (item is MenuItem mi)
                {
                    mi.IsChecked = string.Equals(mi.Tag as string, currentHex, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        private void ClearColorMenuCheckmarks()
        {
            if (AccentColorMenuItem == null) return;
            foreach (var item in AccentColorMenuItem.Items)
            {
                if (item is MenuItem mi) mi.IsChecked = false;
            }
        }

        private void RgbCycle_Click(object sender, RoutedEventArgs e)
        {
            _settings.RgbSpectrumCycle = RgbCycleMenuItem.IsChecked;
            if (_settings.RgbSpectrumCycle)
            {
                ClearColorMenuCheckmarks();
            }
            else
            {
                UpdateColorMenuCheckmarks(_settings.AccentColorHex);
            }
            SaveCurrentSettings();
        }

        private void RgbCycleSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _settings.RgbCycleSpeed = e.NewValue;
            if (RgbCycleSpeedValueText != null)
                RgbCycleSpeedValueText.Text = $"{Math.Round(e.NewValue, 1)}x";
            SaveCurrentSettings();
        }

        private void RainbowBorder_Click(object sender, RoutedEventArgs e)
        {
            _settings.RainbowBorderEnabled = RainbowBorderMenuItem.IsChecked;
            UpdateRainbowBorderState();
            SaveCurrentSettings();
        }

        private void BorderWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _settings.BorderWidth = e.NewValue;
            if (BorderWidthValueText != null)
                BorderWidthValueText.Text = $"{Math.Round(e.NewValue)}px";
            UpdateRainbowBorderState();
            SaveCurrentSettings();
        }

        private void CrtScanlines_Click(object sender, RoutedEventArgs e)
        {
            _settings.CrtScanlinesEnabled = CrtScanlinesMenuItem.IsChecked;
            UpdateScanlinesState();
            SaveCurrentSettings();
        }

        private void ScanlineThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _settings.ScanlineThickness = e.NewValue;
            if (ScanlineThicknessValueText != null)
                ScanlineThicknessValueText.Text = $"{Math.Round(e.NewValue)}px";
            UpdateScanlinesState();
            SaveCurrentSettings();
        }

        private void CrtGlitch_Click(object sender, RoutedEventArgs e)
        {
            _settings.CrtGlitchEnabled = CrtGlitchMenuItem.IsChecked;
            UpdateGlitchState();
            SaveCurrentSettings();
        }

        private void GlitchChanceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _settings.GlitchChance = e.NewValue;
            if (GlitchChanceValueText != null)
                GlitchChanceValueText.Text = $"{Math.Round(e.NewValue)}%";
            SaveCurrentSettings();
        }

        private void CrtSnow_Click(object sender, RoutedEventArgs e)
        {
            _settings.CrtSnowEnabled = CrtSnowMenuItem.IsChecked;
            UpdateSnowState();
            SaveCurrentSettings();
        }

        private void SnowAmountSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _settings.SnowAmount = e.NewValue;
            if (SnowAmountValueText != null)
                SnowAmountValueText.Text = $"{Math.Round(e.NewValue)}%";
            UpdateSnowState();
            SaveCurrentSettings();
        }

        private void ShowLabels_Click(object sender, RoutedEventArgs e)
        {
            _settings.ShowBlipLabels = ShowLabelsMenuItem.IsChecked;
            SaveCurrentSettings();
        }

        private void ShowTelemetry_Click(object sender, RoutedEventArgs e)
        {
            _settings.ShowDigitalTelemetry = ShowTelemetryMenuItem.IsChecked;
            SaveCurrentSettings();
        }

        private void GlassOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _settings.WindowGlassOpacity = e.NewValue;
            if (MainRadarContainer != null) MainRadarContainer.Opacity = e.NewValue;
            if (GlassOpacityValueText != null)
                GlassOpacityValueText.Text = $"{Math.Round(e.NewValue * 100)}%";
            SaveCurrentSettings();
        }

        private void LedBrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _settings.LedBrightness = e.NewValue;
            if (LedBrightnessValueText != null)
                LedBrightnessValueText.Text = $"{Math.Round(e.NewValue * 100)}%";
            SaveCurrentSettings();
        }

        private void AlwaysOnTop_Click(object sender, RoutedEventArgs e)
        {
            _settings.AlwaysOnTop = AlwaysOnTopMenuItem.IsChecked;
            Topmost = _settings.AlwaysOnTop;
            SaveCurrentSettings();
        }

        private void Shadow_Click(object sender, RoutedEventArgs e)
        {
            _settings.WindowShadow = ShadowMenuItem.IsChecked;
            if (RadarDropShadow != null)
                RadarDropShadow.Opacity = _settings.WindowShadow ? 0.40 : 0.0;
            SaveCurrentSettings();
        }

        private void ResetSize_Click(object sender, RoutedEventArgs e)
        {
            Width = 460;
            Height = 460;
            EnsureWindowVisibleOnScreen();
            SaveCurrentSettings();
        }

        private void CloseWidget_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ExitAll_Click(object sender, RoutedEventArgs e)
        {
            App.CleanProcessExit(0);
        }

        private void Slider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is Slider slider)
            {
                double step = slider.SmallChange > 0 ? slider.SmallChange : 1.0;
                slider.Value = Math.Clamp(slider.Value + (e.Delta > 0 ? step : -step), slider.Minimum, slider.Maximum);
                e.Handled = true;
            }
        }

        #endregion

        #region Menu Traversal Smoothness Helpers

        private void EffectsMenuItem_MouseEnter(object sender, MouseEventArgs e)
        {
            _effectsCloseTimer.Stop();
        }

        private void EffectsMenuItem_MouseLeave(object sender, MouseEventArgs e)
        {
            if (EffectsMenuItem.IsSubmenuOpen)
                _effectsCloseTimer.Start();
        }

        private void EffectsMenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            _effectsCloseTimer.Stop();
            AttachPopupLeaveHandler(EffectsMenuItem);
        }

        private void EffectsMenuItem_SubmenuClosed(object sender, RoutedEventArgs e)
        {
            _effectsCloseTimer.Stop();
        }

        private void EffectsCloseTimer_Tick(object? sender, EventArgs e)
        {
            _effectsCloseTimer.Stop();
            if (EffectsMenuItem != null && !EffectsMenuItem.IsMouseOver)
                EffectsMenuItem.IsSubmenuOpen = false;
        }

        private void AttachPopupLeaveHandler(MenuItem item)
        {
            try
            {
                if (item.Template?.FindName("PART_Popup", item) is Popup popup &&
                    popup.Child is FrameworkElement popupChild)
                {
                    if (_subscribedPopupChild != popupChild)
                    {
                        if (_subscribedPopupChild != null)
                        {
                            _subscribedPopupChild.MouseEnter -= PopupChild_MouseEnter;
                            _subscribedPopupChild.MouseLeave -= PopupChild_MouseLeave;
                        }

                        _subscribedPopupChild = popupChild;
                        _subscribedPopupChild.MouseEnter += PopupChild_MouseEnter;
                        _subscribedPopupChild.MouseLeave += PopupChild_MouseLeave;
                    }
                }
            }
            catch { }
        }

        private void PopupChild_MouseEnter(object sender, MouseEventArgs e)
        {
            _effectsCloseTimer.Stop();
        }

        private void PopupChild_MouseLeave(object sender, MouseEventArgs e)
        {
            _effectsCloseTimer.Start();
        }

        private void MainContextMenu_PreviewMouseMove(object sender, MouseEventArgs e) { }
        private void MainContextMenu_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { }
        private void MainContextMenu_Closed(object sender, RoutedEventArgs e)
        {
            _effectsCloseTimer.Stop();
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            _isClosed = true;
            CompositionTarget.Rendering -= OnCompositionRendering;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _glitchTimer.Stop();
            _snowTimer.Stop();
            _effectsCloseTimer.Stop();
            _networkService.Dispose();
            _renderer.Dispose();
            base.OnClosed(e);
        }
    }
}