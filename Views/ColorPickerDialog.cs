using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FencesWPF.Views
{
    /// <summary>
    /// Lightweight visual color picker — HSV wheel + brightness/alpha sliders.
    /// Usage: var dlg = new ColorPickerDialog(initialColor); dlg.ShowDialog();
    ///        if (dlg.Confirmed) use dlg.SelectedColor;
    /// </summary>
    public partial class ColorPickerDialog : Window
    {
        // ── Result ─────────────────────────────────────────────────────────────
        public bool  Confirmed     { get; private set; }
        public Color SelectedColor { get; private set; } = Colors.White;

        // ── HSV state ─────────────────────────────────────────────────────────
        private double _hue        = 0;    // 0-360
        private double _saturation = 1;    // 0-1
        private double _value      = 1;    // 0-1
        private double _alpha      = 1;    // 0-1

        // ── Wheel bitmap dims ─────────────────────────────────────────────────
        private const int WheelSize = 200;
        private WriteableBitmap? _wheelBitmap;
        private bool _draggingWheel;
        private bool _loading = true;

        public ColorPickerDialog(Color initial)
        {
            RgbToHsv(initial.R, initial.G, initial.B,
                     out _hue, out _saturation, out _value);
            _alpha = initial.A / 255.0;
            BuildUI();
            _loading = false;
            UpdateAll();
        }

        // ── Build UI in code (no XAML needed for this dialog) ─────────────────
        private Canvas _wheelCanvas  = new();
        private Ellipse _wheelCursor = new();
        private Slider  _slBrightness = new();
        private Slider  _slAlpha      = new();
        private Border  _preview      = new();
        private TextBox _tbHex        = new();
        private Image   _wheelImg     = new();

        private void BuildUI()
        {
            Width  = 300;
            Height = 420;
            WindowStyle          = WindowStyle.None;
            AllowsTransparency   = true;
            Background           = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode           = ResizeMode.NoResize;

            var root = new Border
            {
                Background    = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x28)),
                CornerRadius  = new CornerRadius(14),
                BorderBrush   = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
            };
            root.Effect = new System.Windows.Media.Effects.DropShadowEffect
            { Color = Colors.Black, BlurRadius = 30, ShadowDepth = 4, Opacity = 0.6 };

            var sp = new StackPanel { Margin = new Thickness(16) };

            // ── Title bar ─────────────────────────────────────────────────────
            var titleBar = new Grid { Margin = new Thickness(-16, -16, -16, 12), Height = 40 };
            titleBar.Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x0D, 0x22));
            var titleBorder = new Border
            {
                CornerRadius = new CornerRadius(14, 14, 0, 0),
                Background   = new SolidColorBrush(Color.FromRgb(0x0D, 0x0D, 0x22))
            };
            titleBar.Children.Add(titleBorder);
            var titleTb = new TextBlock
            {
                Text               = "🎨  Selector de Color",
                Foreground         = Brushes.White,
                FontSize           = 12,
                FontWeight         = FontWeights.SemiBold,
                VerticalAlignment  = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin             = new Thickness(16, 0, 0, 0)
            };
            titleBar.Children.Add(titleTb);
            var btnClose = new Button
            {
                Content             = "✕",
                Width               = 32, Height = 32,
                Background          = Brushes.Transparent,
                Foreground          = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0xA0)),
                BorderThickness     = new Thickness(0),
                Cursor              = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Center,
                Margin              = new Thickness(0, 0, 8, 0),
                FontSize            = 13
            };
            btnClose.Click += (_, _) => { Confirmed = false; Close(); };
            titleBar.Children.Add(btnClose);
            titleBar.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
            sp.Children.Add(titleBar);

            // ── Wheel ─────────────────────────────────────────────────────────
            _wheelBitmap = new WriteableBitmap(WheelSize, WheelSize, 96, 96,
                PixelFormats.Bgra32, null);
            DrawWheel();
            _wheelImg = new Image
            {
                Source = _wheelBitmap,
                Width  = WheelSize,
                Height = WheelSize
            };

            _wheelCursor = new Ellipse
            {
                Width           = 12, Height = 12,
                Stroke          = Brushes.White,
                StrokeThickness = 2,
                Fill            = Brushes.Transparent
            };

            _wheelCanvas = new Canvas
            {
                Width  = WheelSize, Height = WheelSize,
                Cursor = Cursors.Cross,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _wheelCanvas?.Children.Add(_wheelImg);
            _wheelCanvas?.Children.Add(_wheelCursor);
            _wheelCanvas.MouseLeftButtonDown += Wheel_MouseDown;
            _wheelCanvas.MouseMove           += Wheel_MouseMove;
            _wheelCanvas.MouseLeftButtonUp   += Wheel_MouseUp;
            sp.Children.Add(_wheelCanvas);

            // ── Sliders ───────────────────────────────────────────────────────
            sp.Children.Add(MakeLabel("Brillo"));
            _slBrightness = MakeSlider(0, 1, _value);
            _slBrightness.ValueChanged += (_, _) =>
            {
                if (_loading) return;
                _value = _slBrightness.Value;
                UpdateAll();
            };
            sp.Children.Add(_slBrightness);

            sp.Children.Add(MakeLabel("Opacidad (Alpha)"));
            _slAlpha = MakeSlider(0, 1, _alpha);
            _slAlpha.ValueChanged += (_, _) =>
            {
                if (_loading) return;
                _alpha = _slAlpha.Value;
                UpdateAll();
            };
            sp.Children.Add(_slAlpha);

            // ── Hex + Preview ─────────────────────────────────────────────────
            var hexRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            hexRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hexRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });

            _tbHex = new TextBox
            {
                Background      = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                Foreground      = Brushes.White,
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5A)),
                BorderThickness = new Thickness(1),
                Padding         = new Thickness(6, 4, 6, 4),
                CaretBrush      = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily      = new FontFamily("Consolas")
            };
            _tbHex.LostFocus += TbHex_LostFocus;
            Grid.SetColumn(_tbHex, 0);
            hexRow.Children.Add(_tbHex);

            _preview = new Border
            {
                Margin          = new Thickness(8, 0, 0, 0),
                CornerRadius    = new CornerRadius(8),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5A)),
                BorderThickness = new Thickness(1)
            };
            Grid.SetColumn(_preview, 1);
            hexRow.Children.Add(_preview);
            sp.Children.Add(hexRow);

            // ── Buttons ───────────────────────────────────────────────────────
            var btnRow = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin              = new Thickness(0, 14, 0, 0)
            };

            var btnOk = MakeButton("Aceptar", Color.FromRgb(0x1A, 0x4A, 0x8A));
            btnOk.Click += (_, _) => { Confirmed = true; Close(); };
            var btnCancel = MakeButton("Cancelar", Color.FromRgb(0x2A, 0x2A, 0x40));
            btnCancel.Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xC0));
            btnCancel.Click += (_, _) => { Confirmed = false; Close(); };

            btnRow.Children.Add(btnOk);
            btnRow.Children.Add(btnCancel);
            
            sp.Children.Add(btnRow);

            root.Child  = sp;
            Content     = root;
        }

        // ── Wheel drawing ──────────────────────────────────────────────────────
        private unsafe void DrawWheel()
        {
            if (_wheelBitmap == null) return;
            int   size   = WheelSize;
            float center = size / 2f;
            float radius = center - 2;

            _wheelBitmap.Lock();
            var buf  = (byte*)_wheelBitmap.BackBuffer;
            int stride = _wheelBitmap.BackBufferStride;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx  = x - center;
                    float dy  = y - center;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);

                    if (dist > radius)
                    {
                        // Outside circle — transparent
                        byte* px = buf + y * stride + x * 4;
                        px[0] = px[1] = px[2] = px[3] = 0;
                        continue;
                    }

                    float hue = (MathF.Atan2(dy, dx) + MathF.PI) / (2 * MathF.PI) * 360f;
                    float sat = dist / radius;
                    HsvToRgb(hue, sat, _value, out byte r, out byte g, out byte b);

                    byte* pixel = buf + y * stride + x * 4;
                    pixel[0] = b;
                    pixel[1] = g;
                    pixel[2] = r;
                    pixel[3] = 255;
                }
            }

            _wheelBitmap.AddDirtyRect(new Int32Rect(0, 0, size, size));
            _wheelBitmap.Unlock();
        }

        // ── Wheel interaction ─────────────────────────────────────────────────
        private void Wheel_MouseDown(object s, MouseButtonEventArgs e)
        {
            _draggingWheel = true;
            _wheelCanvas.CaptureMouse();
            PickFromWheel(e.GetPosition(_wheelCanvas));
        }

        private void Wheel_MouseMove(object s, MouseEventArgs e)
        {
            if (!_draggingWheel) return;
            PickFromWheel(e.GetPosition(_wheelCanvas));
        }

        private void Wheel_MouseUp(object s, MouseButtonEventArgs e)
        {
            _draggingWheel = false;
            _wheelCanvas.ReleaseMouseCapture();
        }

        private void PickFromWheel(Point pos)
        {
            float cx  = WheelSize / 2f;
            float cy  = WheelSize / 2f;
            float dx  = (float)pos.X - cx;
            float dy  = (float)pos.Y - cy;
            float rad = WheelSize / 2f - 2;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            _saturation = Math.Min(1f, dist / rad);
            _hue        = (MathF.Atan2(dy, dx) + MathF.PI) / (2 * MathF.PI) * 360f;

            UpdateAll();
        }

        // ── Hex input ─────────────────────────────────────────────────────────
        private void TbHex_LostFocus(object s, RoutedEventArgs e)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(_tbHex.Text)!;
                RgbToHsv(c.R, c.G, c.B, out _hue, out _saturation, out _value);
                _alpha   = c.A / 255.0;
                _loading = true;
                _slBrightness.Value = _value;
                _slAlpha.Value      = _alpha;
                _loading = false;
                DrawWheel();
                UpdateCursorPos();
                UpdatePreviewAndColor();
            }
            catch { }
        }

        // ── Update everything ─────────────────────────────────────────────────
        private void UpdateAll()
        {
            DrawWheel();
            UpdateCursorPos();
            UpdatePreviewAndColor();
        }

        private void UpdateCursorPos()
        {
            float cx  = WheelSize / 2f;
            float cy  = WheelSize / 2f;
            float rad = WheelSize / 2f - 2;
            float angle = (float)(_hue * Math.PI / 180.0);
            float x = cx + MathF.Cos(angle) * (float)_saturation * rad;
            float y = cy + MathF.Sin(angle) * (float)_saturation * rad;

            Canvas.SetLeft(_wheelCursor, x - 6);
            Canvas.SetTop(_wheelCursor,  y - 6);
        }

        private void UpdatePreviewAndColor()
        {
            HsvToRgb(_hue, _saturation, _value, out byte r, out byte g, out byte b);
            byte a = (byte)(_alpha * 255);
            SelectedColor = Color.FromArgb(a, r, g, b);
            _preview.Background = new SolidColorBrush(SelectedColor);
            _tbHex.Text = $"#{a:X2}{r:X2}{g:X2}{b:X2}";

            // Update brightness slider gradient background
            HsvToRgb(_hue, _saturation, 1.0, out byte rFull, out byte gFull, out byte bFull);
            var slBg = new LinearGradientBrush(
                Color.FromRgb(0, 0, 0),
                Color.FromRgb(rFull, gFull, bFull),
                0);
            _slBrightness.Background = slBg;
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static TextBlock MakeLabel(string text) => new TextBlock
        {
            Text       = text,
            Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xC0)),
            FontSize   = 10,
            Margin     = new Thickness(0, 8, 0, 2)
        };

        private static Slider MakeSlider(double min, double max, double val) => new Slider
        {
            Minimum            = min,
            Maximum            = max,
            Value              = val,
            VerticalAlignment  = VerticalAlignment.Center,
            Margin             = new Thickness(0, 0, 0, 0)
        };

        private static Button MakeButton(string text, Color bg) => new Button
        {
            Content         = text,
            Padding         = new Thickness(14, 7, 14, 7),
            Margin          = new Thickness(0, 0, 8, 0),
            Background      = new SolidColorBrush(bg),
            Foreground      = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor          = Cursors.Hand,
        };

        // ── HSV / RGB conversions ─────────────────────────────────────────────
        private static void HsvToRgb(double h, double s, double v,
                                     out byte r, out byte g, out byte b)
        {
            h = ((h % 360) + 360) % 360;
            int   i = (int)(h / 60) % 6;
            double f = h / 60 - Math.Floor(h / 60);
            double p = v * (1 - s);
            double q = v * (1 - f * s);
            double t = v * (1 - (1 - f) * s);

            (double dr, double dg, double db) = i switch
            {
                0 => (v, t, p),
                1 => (q, v, p),
                2 => (p, v, t),
                3 => (p, q, v),
                4 => (t, p, v),
                _ => (v, p, q)
            };

            r = (byte)(dr * 255);
            g = (byte)(dg * 255);
            b = (byte)(db * 255);
        }

        private static void RgbToHsv(byte ri, byte gi, byte bi,
                                     out double h, out double s, out double v)
        {
            double r = ri / 255.0, g = gi / 255.0, b = bi / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            v = max;
            s = max == 0 ? 0 : delta / max;

            if (delta == 0) { h = 0; return; }
            if (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
            if (h < 0) h += 360;
        }
    }
}
