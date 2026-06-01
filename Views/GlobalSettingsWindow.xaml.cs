using FencesWPF.Models;
using FencesWPF.Services;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FencesWPF.Views
{
    public partial class GlobalSettingsWindow : Window
    {
        private readonly AppSettings _s;
        private bool _loading = true;

        public GlobalSettingsWindow()
        {
            InitializeComponent();
            _s = FenceManager.Instance.Settings;
            LoadSettings();
            TbStoragePath.Text = StorageService.StorageInfo;
            _loading = false;
        }

        // ── Draggable chrome ───────────────────────────────────────────────────
        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }

        // ── Load ───────────────────────────────────────────────────────────────
        private void LoadSettings()
        {
            CbStartWithWindows.IsChecked = _s.StartWithWindows;
            CbAutoSave.IsChecked         = _s.AutoSave;
            CbEnableSnapping.IsChecked   = _s.EnableSnapping;
            TbAutoSaveInterval.Text      = _s.AutoSaveInterval.ToString();
            SliderSnap.Value             = _s.SnapTolerance;
            TbSnap.Text                  = $"{_s.SnapTolerance:0}px";

            // Opacity from DefaultBackground alpha channel
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(_s.DefaultBackground)!;
                double opacity = c.A / 255.0;
                SliderOpacity.Value = Math.Round(opacity * 20) / 20; // snap to 0.05
                TbOpacity.Text = $"{opacity * 100:0}%";
            }
            catch { SliderOpacity.Value = 0.92; TbOpacity.Text = "92%"; }

            TbBgColor.Text     = _s.DefaultBackground;
            TbBorderColor.Text = "#FF4A90D9";
            TbTitleColor.Text  = "#FF2A2A4A";
            UpdatePreviews();

            CbDefaultMode.SelectedIndex = _s.DefaultMode switch
            {
                FenceMode.Static    => 0,
                FenceMode.AutoRoll  => 1,
                FenceMode.Collapsed => 2,
                _                   => 1
            };

            CbDefaultIconSize.SelectedIndex = _s.DefaultIconSize switch
            {
                IconSize.Small => 0,
                IconSize.Large => 2,
                _              => 1
            };
        }

        // ── System checkboxes ─────────────────────────────────────────────────
        private void CbStartWithWindows_Click(object sender, RoutedEventArgs e) =>
            _s.StartWithWindows = CbStartWithWindows.IsChecked == true;

        private void CbAutoSave_Click(object sender, RoutedEventArgs e)
        {
            _s.AutoSave = CbAutoSave.IsChecked == true;
            FenceManager.Instance.RestartAutoSave();
        }

        private void CbEnableSnapping_Click(object sender, RoutedEventArgs e) =>
            _s.EnableSnapping = CbEnableSnapping.IsChecked == true;

        private void TbAutoSaveInterval_LostFocus(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TbAutoSaveInterval.Text, out int val) && val >= 5)
            {
                _s.AutoSaveInterval = val;
                FenceManager.Instance.RestartAutoSave();
            }
            else TbAutoSaveInterval.Text = _s.AutoSaveInterval.ToString();
        }

        private void SliderSnap_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            _s.SnapTolerance = SliderSnap.Value;
            TbSnap.Text = $"{SliderSnap.Value:0}px";
        }

        // ── Appearance ────────────────────────────────────────────────────────
        private void SliderOpacity_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            double op = SliderOpacity.Value;
            TbOpacity.Text = $"{op * 100:0}%";

            // Sync alpha into DefaultBackground
            if (TryParseColor(TbBgColor.Text, out var col))
            {
                col.A = (byte)(op * 255);
                _s.DefaultBackground = ColorToHex(col);
                TbBgColor.Text = _s.DefaultBackground;
            }
        }

        private void TbBgColor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            if (TryParseBrush(TbBgColor.Text, out var br))
            {
                BgPreview.Background = br;
                _s.DefaultBackground = TbBgColor.Text;

                // keep opacity slider in sync
                if (TryParseColor(TbBgColor.Text, out var col))
                {
                    _loading = true;
                    SliderOpacity.Value = col.A / 255.0;
                    TbOpacity.Text = $"{col.A / 255.0 * 100:0}%";
                    _loading = false;
                }
            }
        }

        private void TbBorderColor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            if (TryParseBrush(TbBorderColor.Text, out var br))
                BorderPreview.Background = br;
        }

        private void TbTitleColor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            if (TryParseBrush(TbTitleColor.Text, out var br))
                TitlePreview.Background = br;
        }

        // ── Quick themes ──────────────────────────────────────────────────────
        private void ApplyTheme(string bg, string border, string title)
        {
            _loading = true;
            TbBgColor.Text     = bg;
            TbBorderColor.Text = border;
            TbTitleColor.Text  = title;
            _loading = false;
            _s.DefaultBackground = bg;
            UpdatePreviews();

            if (TryParseColor(bg, out var col))
            {
                SliderOpacity.Value = col.A / 255.0;
                TbOpacity.Text = $"{col.A / 255.0 * 100:0}%";
            }
        }

        private void Theme_Dark(object s,   RoutedEventArgs e) => ApplyTheme("#CC1E1E2E","#FF5A5A7A","#FF2A2A3A");
        private void Theme_Blue(object s,   RoutedEventArgs e) => ApplyTheme("#CC0D1B3E","#FF4A90D9","#FF1A2A5A");
        private void Theme_Green(object s,  RoutedEventArgs e) => ApplyTheme("#CC0D2B1E","#FF4AB94A","#FF1A4A2A");
        private void Theme_Red(object s,    RoutedEventArgs e) => ApplyTheme("#CC2B0D0D","#FFD94A4A","#FF4A1A1A");
        private void Theme_Purple(object s, RoutedEventArgs e) => ApplyTheme("#CC1A0D2B","#FF9B4AD9","#FF2A1A4A");
        private void Theme_Night(object s,  RoutedEventArgs e) => ApplyTheme("#E0070714","#FF204080","#FF0D0D28");

        // ── Defaults ──────────────────────────────────────────────────────────
        private void CbDefaultMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            _s.DefaultMode = CbDefaultMode.SelectedIndex switch
            {
                0 => FenceMode.Static,
                2 => FenceMode.Collapsed,
                _ => FenceMode.AutoRoll
            };
        }

        private void CbDefaultIconSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            _s.DefaultIconSize = CbDefaultIconSize.SelectedIndex switch
            {
                0 => IconSize.Small,
                2 => IconSize.Large,
                _ => IconSize.Medium
            };
        }

        // ── Apply to all ──────────────────────────────────────────────────────
        private void ApplyToAllFences()
        {
            bool parsedBg     = TryParseBrush(TbBgColor.Text,     out var bgBrush);
            bool parsedBorder = TryParseBrush(TbBorderColor.Text, out var borderBrush);
            bool parsedTitle  = TryParseBrush(TbTitleColor.Text,  out var titleBrush);

            foreach (var fence in FenceManager.Instance.Fences)
            {
                if (parsedBg)     fence.MainBorder.Background  = bgBrush;
                if (parsedBorder) fence.MainBorder.BorderBrush = borderBrush;
                if (parsedTitle)  fence.TitleBar.Background    = titleBrush;

                fence.Opacity  = SliderOpacity.Value;
                fence.IconSize = _s.DefaultIconSize;
                fence.ApplyIconSize();
                fence.Mode = _s.DefaultMode;
                fence.ApplyMode();
            }
            FenceManager.Instance.SaveLayout();
        }

        private void BtnApplyAll_Click(object sender, RoutedEventArgs e)
        {
            ApplyToAllFences();
            MessageBox.Show(
                $"Configuración aplicada a {FenceManager.Instance.Fences.Count} panel(es) activo(s).",
                "Configuración Global", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ── Storage ───────────────────────────────────────────────────────────
        private void BtnOpenDataFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string folder = System.IO.Path.GetDirectoryName(StorageService.LayoutFile)!;
                Process.Start("explorer.exe", folder);
            }
            catch { }
        }

        // ── Footer ────────────────────────────────────────────────────────────
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            FenceManager.Instance.SaveSettings();
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // ── Color picker buttons ──────────────────────────────────────────────
        private void BgPreview_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => PickColor(TbBgColor);

        private void BorderPreview_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => PickColor(TbBorderColor);

        private void TitlePreview_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => PickColor(TbTitleColor);

        private void PickColor(TextBox target)
        {
            TryParseColor(target.Text, out var initial);
            var dlg = new ColorPickerDialog(initial) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.Confirmed)
            {
                target.Text = ColorToHex(dlg.SelectedColor);
                UpdatePreviews();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private void UpdatePreviews()
        {
            if (TryParseBrush(TbBgColor.Text,     out var bg)) BgPreview.Background     = bg;
            if (TryParseBrush(TbBorderColor.Text, out var br)) BorderPreview.Background = br;
            if (TryParseBrush(TbTitleColor.Text,  out var ti)) TitlePreview.Background  = ti;
        }

        private static bool TryParseBrush(string hex, out SolidColorBrush brush)
        {
            try { var c = (Color)ColorConverter.ConvertFromString(hex)!; brush = new SolidColorBrush(c); return true; }
            catch { brush = new SolidColorBrush(Colors.Gray); return false; }
        }

        private static bool TryParseColor(string hex, out Color color)
        {
            try { color = (Color)ColorConverter.ConvertFromString(hex)!; return true; }
            catch { color = Colors.Gray; return false; }
        }

        private static string ColorToHex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
