using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CyberPunkNetRadar.UI
{
    public partial class ColorPickerWindow : Window
    {
        private bool _isUpdatingInternally = false;

        public string SelectedHexColor { get; private set; } = "#00F0FF";
        public Color SelectedColor { get; private set; } = Color.FromRgb(0x00, 0xF0, 0xFF);

        public ColorPickerWindow(string initialHex)
        {
            InitializeComponent();
            SetColorFromHex(initialHex);
        }

        private void SetColorFromHex(string hex)
        {
            _isUpdatingInternally = true;
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) hex = "#00F0FF";
                if (!hex.StartsWith("#")) hex = "#" + hex;

                var color = (Color)ColorConverter.ConvertFromString(hex);
                SelectedColor = color;
                SelectedHexColor = hex.ToUpperInvariant();

                RedSlider.Value = color.R;
                GreenSlider.Value = color.G;
                BlueSlider.Value = color.B;

                RedValText.Text = color.R.ToString();
                GreenValText.Text = color.G.ToString();
                BlueValText.Text = color.B.ToString();

                HexInputTextBox.Text = SelectedHexColor;
                PreviewColorBrush.Color = color;
            }
            catch
            {
                // Ignore parse errors while user is typing
            }
            finally
            {
                _isUpdatingInternally = false;
            }
        }

        private void RgbSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingInternally) return;

            byte r = (byte)RedSlider.Value;
            byte g = (byte)GreenSlider.Value;
            byte b = (byte)BlueSlider.Value;

            RedValText.Text = r.ToString();
            GreenValText.Text = g.ToString();
            BlueValText.Text = b.ToString();

            SelectedColor = Color.FromRgb(r, g, b);
            SelectedHexColor = $"#{r:X2}{g:X2}{b:X2}";

            _isUpdatingInternally = true;
            HexInputTextBox.Text = SelectedHexColor;
            PreviewColorBrush.Color = SelectedColor;
            _isUpdatingInternally = false;
        }

        private void HexInputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingInternally) return;

            string text = HexInputTextBox.Text.Trim();
            if (text.Length == 6 || text.Length == 7)
            {
                if (!text.StartsWith("#")) text = "#" + text;
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(text);
                    SelectedColor = color;
                    SelectedHexColor = text.ToUpperInvariant();

                    _isUpdatingInternally = true;
                    RedSlider.Value = color.R;
                    GreenSlider.Value = color.G;
                    BlueSlider.Value = color.B;
                    RedValText.Text = color.R.ToString();
                    GreenValText.Text = color.G.ToString();
                    BlueValText.Text = color.B.ToString();
                    PreviewColorBrush.Color = color;
                    _isUpdatingInternally = false;
                }
                catch { }
            }
        }

        private void PresetSwatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string hex)
            {
                SetColorFromHex(hex);
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

