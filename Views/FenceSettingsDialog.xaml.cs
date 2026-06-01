using FencesWPF.Models;
using FencesWPF.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// Resolve WinForms vs WPF conflicts
using Brush  = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace FencesWPF.Views
{
    public partial class FenceSettingsDialog : Window
    {
        private readonly FencePanel _fence;
        private bool _loading = true;

        public FenceSettingsDialog(FencePanel fence)
        {
            InitializeComponent();
            _fence = fence;
            LoadSettings();
            _loading = false;
        }

        private void LoadSettings()
        {
            // Opacity
            SliderOpacity.Value = _fence.Opacity;
            TbOpacity.Text = $"{_fence.Opacity * 100:0}%";

            // Colors
            TbBgColor.Text = ColorToHex(_fence.MainBorder.Background);
            TbBorderColor.Text = ColorToHex(_fence.MainBorder.BorderBrush);
            TbTitleColor.Text = ColorToHex(_fence.TitleBar.Background);
            UpdatePreviews();

            // Mode
            switch (_fence.Mode)
            {
                case FenceMode.Static:    RbStatic.IsChecked    = true; break;
                case FenceMode.AutoRoll:  RbAutoRoll.IsChecked  = true; break;
                case FenceMode.Collapsed: RbCollapsed.IsChecked = true; break;
            }

            // Icon size
            switch (_fence.IconSize)
            {
                case IconSize.Small:  RbSmall.IsChecked  = true; break;
                case IconSize.Medium: RbMedium.IsChecked = true; break;
                case IconSize.Large:  RbLarge.IsChecked  = true; break;
            }
        }

        // ── Events ─────────────────────────────────────────────────────────────
        private void SliderOpacity_ValueChanged(object sender,
            System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            TbOpacity.Text = $"{SliderOpacity.Value * 100:0}%";
            ApplyLivePreview();
            _fence.Opacity = SliderOpacity.Value;
            Save();
        }

        private void TbBgColor_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_loading) return;
            if (TryParseBrush(TbBgColor.Text, out var brush))
            {
                _fence.MainBorder.Background = brush;
                BgPreview.Background = brush;
                Save();
            }
        }

        private void TbBorderColor_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_loading) return;
            if (TryParseBrush(TbBorderColor.Text, out var brush))
            {
                _fence.MainBorder.BorderBrush = brush;
                BorderPreview.Background = brush;
                Save();
            }
        }

        private void TbTitleColor_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_loading) return;
            if (TryParseBrush(TbTitleColor.Text, out var brush))
            {
                _fence.TitleBar.Background = brush;
                TitlePreview.Background = brush;
                Save();
            }
        }

        private void RbMode_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (RbStatic.IsChecked    == true) _fence.Mode = FenceMode.Static;
            else if (RbAutoRoll.IsChecked == true) _fence.Mode = FenceMode.AutoRoll;
            else _fence.Mode = FenceMode.Collapsed;
            _fence.ApplyMode();
            Save();
        }

        private void RbIconSize_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (RbSmall.IsChecked  == true) _fence.IconSize = IconSize.Small;
            else if (RbLarge.IsChecked == true) _fence.IconSize = IconSize.Large;
            else _fence.IconSize = IconSize.Medium;
            _fence.ApplyIconSize();
            Save();
        }

        // ── Themes ─────────────────────────────────────────────────────────────
        private void ApplyTheme(string bg, string border, string title)
        {
            _loading = true;
            TbBgColor.Text     = bg;
            TbBorderColor.Text = border;
            TbTitleColor.Text  = title;
            _loading = false;

            TryParseBrush(bg,     out var bgBrush);
            TryParseBrush(border, out var borBrush);
            TryParseBrush(title,  out var titBrush);

            _fence.MainBorder.Background  = bgBrush;
            _fence.MainBorder.BorderBrush = borBrush;
            _fence.TitleBar.Background    = titBrush;
            BgPreview.Background     = bgBrush;
            BorderPreview.Background  = borBrush;
            TitlePreview.Background   = titBrush;
            Save();
        }

        private void Theme_Dark(object s, RoutedEventArgs e)   => ApplyTheme("#CC1E1E2E","#FF5A5A7A","#FF2A2A3A");
        private void Theme_Blue(object s, RoutedEventArgs e)   => ApplyTheme("#CC0D1B3E","#FF4A90D9","#FF1A2A5A");
        private void Theme_Green(object s, RoutedEventArgs e)  => ApplyTheme("#CC0D2B1E","#FF4AB94A","#FF1A4A2A");
        private void Theme_Red(object s, RoutedEventArgs e)    => ApplyTheme("#CC2B0D0D","#FFD94A4A","#FF4A1A1A");
        private void Theme_Purple(object s, RoutedEventArgs e) => ApplyTheme("#CC1A0D2B","#FF9B4AD9","#FF2A1A4A");
        private void Theme_Night(object s, RoutedEventArgs e)  => ApplyTheme("#E0070714","#FF204080","#FF0D0D28");

        // ── Delete / Reset ─────────────────────────────────────────────────────
        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("¿Eliminar este fence permanentemente?",
                "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                FenceManager.Instance.RemoveFence(_fence);
                this.Close();
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            _loading = true;
            SliderOpacity.Value    = 0.92;
            TbBgColor.Text         = "#CC1E1E2E";
            TbBorderColor.Text     = "#FF4A90D9";
            TbTitleColor.Text      = "#FF2A2A4A";
            RbAutoRoll.IsChecked   = true;
            RbMedium.IsChecked     = true;
            _loading = false;

            _fence.Opacity = 0.92;
            _fence.Mode    = FenceMode.AutoRoll;
            _fence.IconSize = IconSize.Medium;
            TryParseBrush("#CC1E1E2E", out var bg);
            TryParseBrush("#FF4A90D9", out var br);
            TryParseBrush("#FF2A2A4A", out var ti);
            _fence.MainBorder.Background  = bg;
            _fence.MainBorder.BorderBrush = br;
            _fence.TitleBar.Background    = ti;
            _fence.ApplyMode();
            _fence.ApplyIconSize();
            UpdatePreviews();
            Save();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => this.Close();

        // ── Helpers ────────────────────────────────────────────────────────────
        private void Save() => FenceManager.Instance.SaveLayout();

        private void UpdatePreviews()
        {
            TryParseBrush(TbBgColor.Text,     out var bg); BgPreview.Background    = bg;
            TryParseBrush(TbBorderColor.Text, out var br); BorderPreview.Background = br;
            TryParseBrush(TbTitleColor.Text,  out var ti); TitlePreview.Background  = ti;
        }

        // ── Live preview ──────────────────────────────────────────────────────
        private void ApplyLivePreview()
        {
            if (_loading) return;
            try
            {
                if (TryParseBrush(TbBgColor.Text,     out var bg))     _fence.MainBorder.Background  = bg;
                if (TryParseBrush(TbBorderColor.Text, out var border))  _fence.MainBorder.BorderBrush = border;
                if (TryParseBrush(TbTitleColor.Text,  out var title))   _fence.TitleBar.Background    = title;
                _fence.Opacity = SliderOpacity.Value;
                UpdatePreviews();
            }
            catch { }
        }

        // ── Color picker ──────────────────────────────────────────────────────
        private void PickColor(TextBox target)
        {
            try
            {
                var initial = (Color)ColorConverter.ConvertFromString(target.Text)!;
                var dlg = new ColorPickerDialog(initial) { Owner = this };
                if (dlg.ShowDialog() == true && dlg.Confirmed)
                {
                    target.Text = $"#{dlg.SelectedColor.A:X2}{dlg.SelectedColor.R:X2}{dlg.SelectedColor.G:X2}{dlg.SelectedColor.B:X2}";
                    ApplyLivePreview();
                }
            }
            catch { }
        }

        private static bool TryParseBrush(string hex, out SolidColorBrush brush)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex)!;
                brush = new SolidColorBrush(c);
                return true;
            }
            catch { brush = new SolidColorBrush(Colors.Gray); return false; }
        }

        private static string ColorToHex(System.Windows.Media.Brush? brush)
        {
            if (brush is SolidColorBrush scb)
            {
                var c = scb.Color;
                return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            return "#CC1E1E2E";
        }
    }
}
