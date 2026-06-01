using FencesWPF.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FencesWPF.Views
{
    /// <summary>
    /// Full-screen transparent overlay showing the virtual snap grid.
    /// Appears while the user is resizing or moving a fence, disappears 600ms after.
    /// Singleton — one instance shared by all panels.
    /// </summary>
    public class GridOverlayWindow : Window
    {
        // ── Singleton ──────────────────────────────────────────────────────────
        private static GridOverlayWindow? _instance;
        public static GridOverlayWindow Instance =>
            _instance ??= new GridOverlayWindow();

        // ── UI ─────────────────────────────────────────────────────────────────
        private readonly Canvas _canvas = new();

        // ── Native ─────────────────────────────────────────────────────────────
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndAfter,
            int x, int y, int cx, int cy, uint uFlags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int idx);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int idx, int val);

        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001,
                           SWP_NOACTIVATE = 0x0010;

        // ── Constructor ───────────────────────────────────────────────────────
        private GridOverlayWindow()
        {
            var wa = SystemParameters.WorkArea;

            Left   = wa.Left;
            Top    = wa.Top;
            Width  = wa.Width;
            Height = wa.Height;

            WindowStyle        = WindowStyle.None;
            AllowsTransparency = true;
            Background         = Brushes.Transparent;
            ShowInTaskbar      = false;
            ResizeMode         = ResizeMode.NoResize;
            IsHitTestVisible   = false;   // clicks pass through
            Topmost            = false;   // managed via SetWindowPos
            Focusable          = false;

            Content = _canvas;

            Loaded += (_, _) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                // WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
                int ex = GetWindowLong(hwnd, -20);
                ex |= 0x00000020 | 0x00080000 | 0x00000080 | 0x08000000;
                SetWindowLong(hwnd, -20, ex);
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            };
        }

        // ── Public API ────────────────────────────────────────────────────────
        /// <summary>Draw the grid and show the overlay.</summary>
        public void ShowForPanel(FencePanel activePanel)
        {
            DrawGrid(activePanel);
            if (!IsVisible) Show();
        }

        public new void Hide()
        {
            _canvas.Children.Clear();
            base.Hide();
        }

        // ── Grid drawing ──────────────────────────────────────────────────────
        private void DrawGrid(FencePanel activePanel)
        {
            _canvas.Children.Clear();

            var settings = FenceManager.Instance.Settings;
            if (!settings.GridSnapping) return;

            var wa   = SystemParameters.WorkArea;
            int cols = Math.Max(1, settings.GridColumns);
            int rows = Math.Max(1, settings.GridRows);

            double cellW = wa.Width  / cols;
            double cellH = wa.Height / rows;

            var lineBrush = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
            var cellHighlightBrush = new SolidColorBrush(Color.FromArgb(0x0A, 0xA8, 0x55, 0xF7));

            // ── Vertical lines ────────────────────────────────────────────────
            for (int c = 0; c <= cols; c++)
            {
                double x = c * cellW;
                var line = new Line
                {
                    X1              = x, Y1 = 0,
                    X2              = x, Y2 = Height,
                    Stroke          = lineBrush,
                    StrokeThickness = c == 0 || c == cols ? 0.5 : 0.5,
                    StrokeDashArray = c == 0 || c == cols
                        ? null
                        : new DoubleCollection { 4, 4 }
                };
                _canvas.Children.Add(line);
            }

            // ── Horizontal lines ──────────────────────────────────────────────
            for (int r = 0; r <= rows; r++)
            {
                double y = r * cellH;
                var line = new Line
                {
                    X1              = 0,     Y1 = y,
                    X2              = Width, Y2 = y,
                    Stroke          = lineBrush,
                    StrokeThickness = 0.5,
                    StrokeDashArray = r == 0 || r == rows
                        ? null
                        : new DoubleCollection { 4, 4 }
                };
                _canvas.Children.Add(line);
            }

            // ── Highlight cells that the active panel occupies ────────────────
            int panelCol1 = (int)Math.Floor((activePanel.Left - wa.Left) / cellW);
            int panelRow1 = (int)Math.Floor((activePanel.Top  - wa.Top)  / cellH);
            int panelCol2 = (int)Math.Ceiling((activePanel.Left + activePanel.Width  - wa.Left) / cellW);
            int panelRow2 = (int)Math.Ceiling((activePanel.Top  + activePanel.Height - wa.Top)  / cellH);

            panelCol1 = Math.Clamp(panelCol1, 0, cols - 1);
            panelRow1 = Math.Clamp(panelRow1, 0, rows - 1);
            panelCol2 = Math.Clamp(panelCol2, 1, cols);
            panelRow2 = Math.Clamp(panelRow2, 1, rows);

            var highlight = new Rectangle
            {
                Width           = (panelCol2 - panelCol1) * cellW,
                Height          = (panelRow2 - panelRow1) * cellH,
                Fill            = cellHighlightBrush,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(highlight, panelCol1 * cellW);
            Canvas.SetTop(highlight,  panelRow1 * cellH);
            _canvas.Children.Add(highlight);

            // ── Cell size label ───────────────────────────────────────────────
            int spanCols = panelCol2 - panelCol1;
            int spanRows = panelRow2 - panelRow1;
            var label = new Border
            {
                Background      = new SolidColorBrush(Color.FromArgb(0xCC, 0x0F, 0x0C, 0x29)),
                CornerRadius    = new CornerRadius(6),
                Padding         = new Thickness(8, 4, 8, 4),
                IsHitTestVisible = false
            };
            label.Child = new TextBlock
            {
                Text       = $"{spanCols} × {spanRows}",
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xA8, 0x55, 0xF7)),
                FontSize   = 11,
                FontWeight = FontWeights.SemiBold
            };
            Canvas.SetLeft(label, activePanel.Left - wa.Left + 8);
            Canvas.SetTop(label,  activePanel.Top  - wa.Top  + 8);
            _canvas.Children.Add(label);
        }
    }
}
