using FencesWPF.Models;
using FencesWPF.Services;
using FontAwesome.Sharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;
using DragEventArgs = System.Windows.DragEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace FencesWPF.Views
{
    public partial class FencePanel : Window
    {
        // ── Constants ──────────────────────────────────────────────────────────
        private const double TitleBarHeight = 36;
        private const int ResizeBorder = 8;

        private const int HT_LEFT = 10;
        private const int HT_RIGHT = 11;
        private const int HT_TOP = 12;
        private const int HT_TOPLEFT = 13;
        private const int HT_TOPRIGHT = 14;
        private const int HT_BOTTOM = 15;
        private const int HT_BOTTOMLEFT = 16;
        private const int HT_BOTTOMRIGHT = 17;

        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private static readonly IntPtr HWND_BOTTOM  = new IntPtr(1);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        // ── State ──────────────────────────────────────────────────────────────
        private bool _isCollapsed = false;
        private bool _isPinned = false;
        private bool _isLocked = false;
        private bool _isDragging = false;
        private bool _isMoving = false;
        private bool _isPanelDeleted = false;
        private bool _snapApplied = false;  // guards against double-snap on title-bar drag

        // FIX 3: Store the user-defined expanded height instead of using a constant
        private double _expandedHeight = 320;

        public FenceMode Mode { get; set; } = FenceMode.Static;
        public IconSize IconSize { get; set; } = IconSize.Medium;
        public string FenceTitle { get; set; } = "Nuevo Fence";
        public ObservableCollection<FenceShortcut> Shortcuts { get; } = new();

        public event EventHandler<FencePanel>? PanelDeleted;

        // ── Peek state ─────────────────────────────────────────────────────────
        private bool   _isPeeking         = false;
        private double _opacityBeforePeek = 1.0;

        // ── Accent color (per-fence glow + dot) ────────────────────────────────
        private Color _accentColor = Color.FromRgb(0xA8, 0x55, 0xF7); // purple default

        // ── Constructor ────────────────────────────────────────────────────────
        public FencePanel(FenceData data)
        {
            InitializeComponent();
            ApplyData(data);
        }

        private void ApplyData(FenceData d)
        {
            FenceTitle = d.Title;
            TitleText.Text = d.Title;
            TitleEdit.Text = d.Title;
            Left = d.X; Top = d.Y;
            Width = d.Width;
            // FIX 3: Persist the custom expanded height
            _expandedHeight = d.IsCollapsed ? d.Height > TitleBarHeight ? d.Height : 320 : d.Height;
            Height = d.Height;
            Opacity = d.Opacity;
            Mode = d.Mode;
            IconSize = d.IconSize;
            _isCollapsed = d.IsCollapsed;
            _isLocked = d.IsLocked;

            TrySetBrush(b => MainBorder.Background = b, d.BackgroundColor, "#CC1A1A2E", 0xCC);
            TrySetBrush(b => MainBorder.BorderBrush = b, d.BorderColor, "#FF4A90D9");
            TrySetBrush(b => TitleBar.Background = b, d.TitleColor, "#FF2D2D44");

            foreach (var s in d.Shortcuts)
                Shortcuts.Add(new FenceShortcut { Name = s.Name, Path = s.Path, Icon = LoadIcon(s.Path) });

            RefreshUI();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            SetupDesktopStyle();
            ApplyIconSize();
            ApplyMode();
            UpdateLockUI();
            ApplyAccentColor(_accentColor);
        }

        /// <summary>Apply accent color to the glow, dot and border.</summary>
        public void ApplyAccentColor(Color c)
        {
            _accentColor = c;

            // Dot fill + glow
            if (AccentDot != null)
            {
                AccentDot.Fill = new SolidColorBrush(c);
                AccentDot.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color       = c,
                    BlurRadius  = 10,
                    ShadowDepth = 0,
                    Opacity     = 0.95
                };
            }

            // Panel outer glow
            if (MainBorder?.Effect is System.Windows.Media.Effects.DropShadowEffect glow)
            {
                glow.Color   = c;
                glow.Opacity = 0.3;
            }

            // Border tint
            MainBorder.BorderBrush = new SolidColorBrush(
                Color.FromArgb(0x45, c.R, c.G, c.B));
        }

        // ── Desktop style ──────────────────────────────────────────────────────
        private void SetupDesktopStyle()
        {
            var helper = new WindowInteropHelper(this);
            helper.EnsureHandle();
            IntPtr hwnd = helper.Handle;

            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);

            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
            // Apply best available glass effect (Acrylic on Win11, BlurBehind on Win10)
            WindowsIntegrationService.ApplyGlassEffect(hwnd);
            // Dark title bar chrome
            WindowsIntegrationService.ApplyDarkTitleBar(hwnd,
                WindowsIntegrationService.Instance.IsDarkMode);
            PushToBottom(hwnd);
            // Reload icons at correct DPI when window moves to a different-DPI monitor
            this.DpiChanged += (_, _) =>
            {
                foreach (var sc in Shortcuts)
                    sc.Icon = LoadIcon(sc.Path);
                RefreshUI();
            };

            // ── Animación de entrada ──────────────────────────────────────────
            PlayEntranceAnimation();
        }

        private static void PushToBottom(IntPtr hwnd) =>
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_SETFOCUS = 0x0007;
            const int WM_NCHITTEST = 0x0084;

            switch (msg)
            {
                case WM_SETFOCUS:
                    if (!_isMoving)
                    {
                        PushToBottom(hwnd);
                        handled = false;
                    }
                    break;

                case WM_NCHITTEST:
                    if (!_isLocked)
                    {
                        int screenX = unchecked((short)(lParam.ToInt32() & 0xFFFF));
                        int screenY = unchecked((short)((lParam.ToInt32() >> 16) & 0xFFFF));

                        GetWindowRect(hwnd, out RECT wr);
                        double scaleX = Width / (wr.Right - wr.Left);
                        double scaleY = Height / (wr.Bottom - wr.Top);
                        double cx = (screenX - wr.Left) * scaleX;
                        double cy = (screenY - wr.Top) * scaleY;

                        int ht = GetEdgeHit(new Point(cx, cy));
                        if (ht != 0)
                        {
                            handled = true;
                            return new IntPtr(ht);
                        }
                    }
                    break;

                case 0x0214: // WM_SIZING — user is actively resizing
                case 0x0216: // WM_MOVING — user is actively moving
                    ShowGridOverlay();
                    break;

                case 0x0232: // WM_EXITSIZEMOVE — user finished resizing/moving
                    if (!_isCollapsed)
                        _expandedHeight = Height;
                    if (_snapApplied)
                    {
                        // Snap already done by TitleBar drag — just save and push to bottom
                        _snapApplied = false;
                    }
                    else
                    {
                        // Resize path — snap grid first, then edges
                        FenceManager.Instance.SnapToGrid(this);
                        FenceManager.Instance.SnapToOtherPanels(this);
                    }
                    PushToBottom(hwnd);
                    FenceManager.Instance.SaveLayout();
                    break;
            }
            return IntPtr.Zero;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isPanelDeleted) return;
        }

        // ── Movement & Resizing ────────────────────────────────────────────────
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) return;
            if (_isLocked || e.ClickCount != 1 || e.ButtonState != MouseButtonState.Pressed) return;
            try { _isMoving = true; DragMove(); }
            catch { }
            finally
            {
                _isMoving = false;
                // Snap ONCE here; WM_EXITSIZEMOVE will skip snap when _snapApplied is true
                _snapApplied = true;
                FenceManager.Instance.SnapToOtherPanels(this);
                FenceManager.Instance.SaveLayout();
                PushToBottom(new WindowInteropHelper(this).Handle);
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(this);
            int ht = GetEdgeHit(pos);
            Cursor = ht == 0 ? Cursors.Arrow : GetResizeCursor(ht);
        }

        private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { }

        private int GetEdgeHit(Point p)
        {
            bool l = p.X <= ResizeBorder, r = p.X >= Width - ResizeBorder;
            bool t = p.Y <= ResizeBorder, b = p.Y >= Height - ResizeBorder;
            if (l && t) return HT_TOPLEFT;
            if (r && t) return HT_TOPRIGHT;
            if (l && b) return HT_BOTTOMLEFT;
            if (r && b) return HT_BOTTOMRIGHT;
            if (l) return HT_LEFT;
            if (r) return HT_RIGHT;
            if (t) return HT_TOP;
            if (b) return HT_BOTTOM;
            return 0;
        }

        private static Cursor GetResizeCursor(int ht) => ht switch
        {
            HT_LEFT or HT_RIGHT => Cursors.SizeWE,
            HT_TOP or HT_BOTTOM => Cursors.SizeNS,
            HT_TOPLEFT or HT_BOTTOMRIGHT => Cursors.SizeNWSE,
            _ => Cursors.SizeNESW
        };

        // ── Title editing ──────────────────────────────────────────────────────
        private void TitleText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;
            e.Handled = true;

            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex & ~WS_EX_NOACTIVATE);

            TitleText.Visibility = Visibility.Collapsed;
            TitleEdit.Text = TitleText.Text;
            TitleEdit.Visibility = Visibility.Visible;
            TitleEdit.Focus();
            TitleEdit.SelectAll();
        }

        private void TitleEdit_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!TitleEdit.IsKeyboardFocusWithin)
                CommitTitle();
        }

        private void TitleEdit_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { CommitTitle(); e.Handled = true; }
            if (e.Key == Key.Escape) { CancelTitle(); }
        }

        private void CommitTitle()
        {
            if (!string.IsNullOrWhiteSpace(TitleEdit.Text))
            {
                FenceTitle = TitleEdit.Text.Trim();
                TitleText.Text = FenceTitle;
            }
            TitleEdit.Visibility = Visibility.Collapsed;
            TitleText.Visibility = Visibility.Visible;

            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE);
            PushToBottom(hwnd);

            FenceManager.Instance.SaveLayout();
        }

        private void CancelTitle()
        {
            TitleEdit.Visibility = Visibility.Collapsed;
            TitleText.Visibility = Visibility.Visible;

            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE);
            PushToBottom(hwnd);
        }

        // ── Buttons ────────────────────────────────────────────────────────────
        private void BtnLock_Click(object sender, RoutedEventArgs e)
        {
            _isLocked = !_isLocked;
            UpdateLockUI();
            FenceManager.Instance.SaveLayout();
        }

        private void UpdateLockUI()
        {
            LockIcon.Icon = _isLocked ? IconChar.Lock : IconChar.LockOpen;
            BtnLock.ToolTip = _isLocked ? "Desbloquear movimiento" : "Bloquear movimiento";
        }

        private void BtnPin_Click(object sender, RoutedEventArgs e)
        {
            _isPinned = !_isPinned;
            BtnPin.ToolTip = _isPinned ? "Desanclar expandido" : "Anclar expandido";

            if (!_isPinned && Mode == FenceMode.AutoRoll) CollapseContent(animate: true);
            else if (_isPinned && _isCollapsed) ExpandContent(animate: true);
            FenceManager.Instance.SaveLayout();
        }

        private void BtnCollapse_Click(object sender, RoutedEventArgs e)
        {
            if (_isCollapsed) ExpandContent(animate: true);
            else CollapseContent(animate: true);
            FenceManager.Instance.SaveLayout();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new FenceSettingsDialog(this) { Owner = this };
            dlg.ShowDialog();
        }

        private void BtnDeletePanel_Click(object sender, RoutedEventArgs e)
        {
            if (_isPanelDeleted) return;

            var result = MessageBox.Show(
                $"¿Estás seguro de que quieres eliminar el panel '{FenceTitle}'?\nEsta acción no se puede deshacer.",
                "Eliminar panel",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _isPanelDeleted = true;
                PanelDeleted?.Invoke(this, this);
                Close();
            }
        }

        // ── Collapse / Expand ──────────────────────────────────────────────────
        public void ApplyMode()
        {
            // Remove previous AutoRoll handlers to avoid duplication
            MainBorder.MouseEnter -= AutoRoll_Expand;
            MainBorder.MouseLeave -= AutoRoll_Collapse;

            switch (Mode)
            {
                case FenceMode.Static:
                    ExpandContent(animate: false);
                    break;
                case FenceMode.Collapsed:
                    CollapseContent(animate: false);
                    break;
                case FenceMode.AutoRoll:
                    CollapseContent(animate: false);
                    MainBorder.MouseEnter += AutoRoll_Expand;
                    MainBorder.MouseLeave += AutoRoll_Collapse;
                    break;
            }
        }

        // ── Grid overlay ──────────────────────────────────────────────────────
        private System.Windows.Threading.DispatcherTimer? _gridOverlayTimer;

        private void ShowGridOverlay()
        {
            // Show the overlay window, then auto-hide 600ms after last resize event
            GridOverlayWindow.Instance.ShowForPanel(this);

            _gridOverlayTimer?.Stop();
            _gridOverlayTimer ??= new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            _gridOverlayTimer.Tick += (_, _) =>
            {
                _gridOverlayTimer.Stop();
                GridOverlayWindow.Instance.Hide();
            };
            _gridOverlayTimer.Start();
        }

        // ── Entrance animation ────────────────────────────────────────────────
        private void PlayEntranceAnimation()
        {
            double targetOpacity = _opacityBeforePeek > 0 ? _opacityBeforePeek : 1.0;

            // Fade in from 0
            Opacity = 0;
            var fade = new DoubleAnimation(0, targetOpacity,
                new Duration(TimeSpan.FromMilliseconds(350)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                BeginTime      = TimeSpan.FromMilliseconds(80)
            };
            BeginAnimation(OpacityProperty, fade);

            // Slide up 12px while fading
            double endY   = Top;
            double startY = endY + 14;
            Top = startY;
            var slide = new DoubleAnimation(startY, endY,
                new Duration(TimeSpan.FromMilliseconds(350)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                BeginTime      = TimeSpan.FromMilliseconds(80)
            };
            BeginAnimation(TopProperty, slide);
        }

        // ── Save feedback toast ────────────────────────────────────────────────
        /// <summary>Shows a brief "Guardado ✓" toast on the panel title bar.</summary>
        public void ShowSaveFeedback()
        {
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    var originalText = TitleText.Text;
                    var originalColor = TitleText.Foreground;

                    TitleText.Text       = $"{originalText}  ✓";
                    TitleText.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xD9, 0x8A));

                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(1200)
                    };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop();
                        TitleText.Text       = originalText;
                        TitleText.Foreground = originalColor;
                    };
                    timer.Start();
                }));
        }

        // ── System theme ──────────────────────────────────────────────────────
        /// <summary>Called by FenceManager when Windows switches dark/light mode.</summary>
        public void ApplySystemTheme(bool isDark)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            WindowsIntegrationService.ApplyDarkTitleBar(hwnd, isDark);

            // Adjust panel background alpha for better contrast on each mode
            if (MainBorder.Background is SolidColorBrush bg)
            {
                var c = bg.Color;
                c.A = (byte)(isDark ? Math.Min(255, c.A + 20) : Math.Max(180, c.A - 10));
                MainBorder.Background = new SolidColorBrush(c);
            }
        }

        // ── Peek ───────────────────────────────────────────────────────────────
        /// <summary>Bring this fence to the front with a fade-in (called by PeekService).</summary>
        public void PeekShow(Duration fadeIn)
        {
            if (_isPeeking) return;
            _isPeeking         = true;
            _opacityBeforePeek = Opacity;

            var hwnd = new WindowInteropHelper(this).Handle;

            // Bring above all normal windows
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

            // Fade to full opacity
            var anim = new DoubleAnimation(_opacityBeforePeek, 1.0, fadeIn)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, anim);
        }

        /// <summary>Return this fence to the desktop with a fade-out (called by PeekService).</summary>
        public void PeekHide(Duration fadeOut)
        {
            if (!_isPeeking) return;
            _isPeeking = false;

            var hwnd = new WindowInteropHelper(this).Handle;

            var anim = new DoubleAnimation(1.0, _opacityBeforePeek, fadeOut)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            anim.Completed += (_, _) =>
            {
                // Return to desktop layer once animation finishes
                PushToBottom(hwnd);
                // Clear animation so Opacity property is writable again
                BeginAnimation(OpacityProperty, null);
                Opacity = _opacityBeforePeek;
            };
            BeginAnimation(OpacityProperty, anim);
        }

        private void AutoRoll_Expand(object sender, MouseEventArgs e)
        {
            if (_isCollapsed) ExpandContent(animate: true);
        }

        private void AutoRoll_Collapse(object sender, MouseEventArgs e)
        {
            if (!_isPinned && !_isDragging) CollapseContent(animate: true);
        }

        private void ExpandContent(bool animate)
        {
            _isCollapsed = false;
            ContentBorder.Visibility = Visibility.Visible;

            CollapseIcon.Icon = IconChar.Minus;
            BtnCollapse.ToolTip = "Colapsar";

            TitleBar.CornerRadius = new CornerRadius(12, 12, 0, 0);
            ContentBorder.CornerRadius = new CornerRadius(0, 0, 12, 12);

            // FIX 3: Use _expandedHeight instead of hardcoded DefaultHeight constant
            double targetHeight = _expandedHeight > TitleBarHeight ? _expandedHeight : 320;

            if (animate)
            {
                var a = new DoubleAnimation(Height, targetHeight,
                    new Duration(TimeSpan.FromMilliseconds(250)))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                BeginAnimation(HeightProperty, a);
            }
            else
            {
                Height = targetHeight;
            }
        }

        private void CollapseContent(bool animate)
        {
            // FIX 3: Save current height before collapsing so it can be restored
            if (!_isCollapsed)
                _expandedHeight = Height;

            _isCollapsed = true;

            CollapseIcon.Icon = IconChar.Plus;
            BtnCollapse.ToolTip = "Expandir";

            TitleBar.CornerRadius = new CornerRadius(12);
            ContentBorder.CornerRadius = new CornerRadius(0);

            if (animate)
            {
                var a = new DoubleAnimation(Height, TitleBarHeight,
                    new Duration(TimeSpan.FromMilliseconds(200)))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
                a.Completed += (_, _) => { ContentBorder.Visibility = Visibility.Collapsed; };
                BeginAnimation(HeightProperty, a);
            }
            else
            {
                Height = TitleBarHeight;
                ContentBorder.Visibility = Visibility.Collapsed;
            }
        }

        // ── Drag & Drop ────────────────────────────────────────────────────────
        private void ContentBorder_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            _isDragging = true;
            if (_isCollapsed) ExpandContent(animate: true);
            DropHint.Visibility = Visibility.Visible;
            e.Effects = DragDropEffects.Link;
            e.Handled = true;
        }

        private void ContentBorder_DragLeave(object sender, DragEventArgs e)
        {
            _isDragging = false;
            DropHint.Visibility = Shortcuts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ContentBorder_Drop(object sender, DragEventArgs e)
        {
            _isDragging = false;
            DropHint.Visibility = Shortcuts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                foreach (var path in (string[])e.Data.GetData(DataFormats.FileDrop)!)
                    AddShortcut(path);
                FenceManager.Instance.SaveLayout();
            }

            if (Mode == FenceMode.AutoRoll && !_isPinned) CollapseContent(animate: true);
        }

        // ── Shortcut Management ────────────────────────────────────────────────
        public void AddShortcut(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return;
            if (Shortcuts.Any(s => s.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;

            Shortcuts.Add(new FenceShortcut
            {
                Name = Path.GetFileNameWithoutExtension(path).TrimEnd(),
                Path = path,
                Icon = LoadIcon(path)
            });

            RefreshUI();
        }

        /// <summary>Force-reload every icon — callable from settings or context menu.</summary>
        public void ReloadAllIcons()
        {
            foreach (var sc in Shortcuts)
                sc.Icon = LoadIcon(sc.Path);
            RefreshUI();
            FenceManager.Instance.SaveLayout();
        }

        private void DeleteShortcut(FenceShortcut sc)
        {
            Shortcuts.Remove(sc);
            RefreshUI();
            FenceManager.Instance.SaveLayout();
        }

        private static void OpenItem(FenceShortcut sc)
        {
            try { Process.Start(new ProcessStartInfo { FileName = sc.Path, UseShellExecute = true }); }
            catch (Exception ex)
            { MessageBox.Show($"Error al abrir:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // ── Item events ────────────────────────────────────────────────────────
        private void ShortcutItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is FenceShortcut sc)
                OpenItem(sc);
        }

        // ── Drag-out support ───────────────────────────────────────────────────
        private Point _dragStart;
        private bool _dragStarted = false;
        private FenceShortcut? _dragCandidate;

        private void ShortcutBorder_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { _dragStarted = false; return; }
            if (sender is not FrameworkElement fe || fe.DataContext is not FenceShortcut sc) return;

            if (!_dragStarted)
            {
                _dragStart   = e.GetPosition(this);
                _dragStarted = true;
                _dragCandidate = sc;
                return;
            }

            // Only start drag once threshold exceeded
            var cur = e.GetPosition(this);
            if (Math.Abs(cur.X - _dragStart.X) < 6 && Math.Abs(cur.Y - _dragStart.Y) < 6) return;

            _dragStarted = false;
            if (_dragCandidate == null) return;

            // Build a DataObject with the file so Windows Explorer / other apps accept it
            var data = new DataObject(DataFormats.FileDrop, new[] { _dragCandidate.Path });
            data.SetData("SourceFence", this);           // marker so we can detect drops back to a fence
            DragDrop.DoDragDrop(fe, data, DragDropEffects.Copy | DragDropEffects.Link | DragDropEffects.Move);
        }

        private void ShortcutBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not Border border) return;

            // Fondo glass en hover
            border.Background      = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
            border.BorderBrush     = new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF));
            border.BorderThickness = new Thickness(1);

            // Escala animada
            var grid = FindVisualChild<Grid>(border, "ShortcutGrid");
            if (grid != null)
            {
                var st = new ScaleTransform(1.0, 1.0);
                grid.RenderTransform       = st;
                grid.RenderTransformOrigin = new Point(0.5, 0.5);
                var anim = new DoubleAnimation(1.0, 1.10,
                    new Duration(TimeSpan.FromMilliseconds(150)))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }

            // Glow de acento en el IconContainer
            var iconContainer = FindVisualChild<Border>(border, "IconContainer");
            if (iconContainer != null)
            {
                iconContainer.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color       = _accentColor,
                    BlurRadius  = 22,
                    ShadowDepth = 0,
                    Opacity     = 0.65
                };
            }

            var btn = FindVisualChild<Button>(border, "BtnDeleteShortcut");
            if (btn != null) btn.Visibility = Visibility.Visible;
        }

        private void ShortcutBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            _dragStarted = false;
            if (sender is not Border border) return;

            border.Background      = Brushes.Transparent;
            border.BorderBrush     = Brushes.Transparent;
            border.BorderThickness = new Thickness(0);

            // Escala de vuelta
            var grid = FindVisualChild<Grid>(border, "ShortcutGrid");
            if (grid?.RenderTransform is ScaleTransform st)
            {
                var anim = new DoubleAnimation(1.10, 1.0,
                    new Duration(TimeSpan.FromMilliseconds(150)))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }

            // Restaurar sombra normal en IconContainer
            var iconContainer = FindVisualChild<Border>(border, "IconContainer");
            if (iconContainer != null)
            {
                iconContainer.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color       = Colors.Black,
                    BlurRadius  = 12,
                    ShadowDepth = 3,
                    Opacity     = 0.4
                };
            }

            var btn = FindVisualChild<Button>(border, "BtnDeleteShortcut");
            if (btn != null) btn.Visibility = Visibility.Collapsed;
        }

        private void BtnDeleteShortcut_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is Button btn && btn.DataContext is FenceShortcut sc)
                DeleteShortcut(sc);
        }

        private void Ctx_Open_Click(object sender, RoutedEventArgs e)
        {
            if (((MenuItem)sender).DataContext is FenceShortcut sc) OpenItem(sc);
        }

        private void Ctx_OpenLocation_Click(object sender, RoutedEventArgs e)
        {
            if (((MenuItem)sender).DataContext is not FenceShortcut sc) return;
            try
            {
                if (File.Exists(sc.Path))
                    Process.Start("explorer.exe", $"/select,\"{sc.Path}\"");
                else if (Directory.Exists(sc.Path))
                    Process.Start("explorer.exe", sc.Path);
            }
            catch { }
        }

        private void Ctx_Rename_Click(object sender, RoutedEventArgs e)
        {
            if (((MenuItem)sender).DataContext is not FenceShortcut sc) return;
            var dlg = new RenameDialog(sc.Name) { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.NewName))
            {
                sc.Name = dlg.NewName.Trim();
                RefreshUI();
                FenceManager.Instance.SaveLayout();
            }
        }

        private void Ctx_ReloadIcon_Click(object sender, RoutedEventArgs e)
        {
            if (((MenuItem)sender).DataContext is not FenceShortcut sc) return;
            ReloadShortcutIcon(sc);
        }

        /// <summary>Reloads the icon for a shortcut. Called by DesktopWatcherService.</summary>
        public void ReloadShortcutIcon(FenceShortcut sc)
        {
            sc.Icon = LoadIcon(sc.Path);
            RefreshUI();
            FenceManager.Instance.SaveLayout();
        }

        private void Ctx_Delete_Click(object sender, RoutedEventArgs e)
        {
            if (((MenuItem)sender).DataContext is FenceShortcut sc)
                DeleteShortcut(sc);
        }

        // ── Icon size ──────────────────────────────────────────────────────────

        // FIX 5 & 6: ApplyIconSize now updates the WrapPanel's ItemWidth/ItemHeight
        // and the icon/container sizes dynamically via the Tag property binding,
        // which the XAML DataTemplate reads to scale correctly.
        public void ApplyIconSize()
        {
            int px = (int)IconSize;           // 32, 48, 64
            int container = px + 4;           // IconContainer border size (36, 52, 68)
            int imgSize   = (int)(px * 0.85); // Image inside container (27, 41, 54)
            int cell      = container + 20;   // WrapPanel cell width  (56, 72, 88)

            // Tag = "cellSize|containerSize|imageSize"  — read by RefreshUI
            if (ContentItems != null)
                ContentItems.Tag = $"{cell}|{container}|{imgSize}";

            RefreshUI();
        }

        // ── UI Refresh ─────────────────────────────────────────────────────────
        public void RefreshUI()
        {
            // Parse sizes from Tag (set by ApplyIconSize)
            int cell = 80, container = 52, img = 40;
            if (ContentItems?.Tag is string tag)
            {
                var parts = tag.Split('|');
                if (parts.Length == 3)
                {
                    int.TryParse(parts[0], out cell);
                    int.TryParse(parts[1], out container);
                    int.TryParse(parts[2], out img);
                }
            }

            // Update WrapPanel dimensions
            var wrapPanel = ContentItems != null ? FindVisualChild<WrapPanel>(ContentItems, null) : null;
            if (wrapPanel != null)
            {
                wrapPanel.ItemWidth  = cell;
                wrapPanel.ItemHeight = cell + 16;
            }

            // ObservableCollection notifica al ItemsControl automáticamente.
            // Solo asignamos ItemsSource la primera vez.
            if (!ReferenceEquals(ContentItems.ItemsSource, Shortcuts))
                ContentItems.ItemsSource = Shortcuts;

            // DropHint: visible solo cuando no hay shortcuts
            if (DropHint != null)
                DropHint.Visibility = Shortcuts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Aplicar tamaños de ícono DESPUÉS del layout pass de WPF
            int capturedContainer = container;
            int capturedImg       = img;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    ApplyIconSizeToItems(capturedContainer, capturedImg);
                    // Actualizar DropHint de nuevo por si cambió durante el layout
                    if (DropHint != null)
                        DropHint.Visibility = Shortcuts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }));
        }

        // Apply icon sizes to all rendered shortcut item containers
        private void ApplyIconSizeToItems(int containerSize, int imageSize)
        {
            // Also update WrapPanel here in case it wasn't ready during RefreshUI
            var wrapPanel = ContentItems != null ? FindVisualChild<WrapPanel>(ContentItems, null) : null;
            if (wrapPanel != null)
            {
                int cell = containerSize + 20;
                wrapPanel.ItemWidth  = cell;
                wrapPanel.ItemHeight = cell + 16;
            }

            for (int i = 0; i < ContentItems.Items.Count; i++)
            {
                var item = ContentItems.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (item == null) continue;

                var iconContainer = FindVisualChild<Border>(item, "IconContainer");
                if (iconContainer != null)
                {
                    iconContainer.Width        = containerSize;
                    iconContainer.Height       = containerSize;
                    iconContainer.CornerRadius = new CornerRadius(containerSize / 4.5);
                }

                var image = FindVisualChild<Image>(item, null);
                if (image != null)
                {
                    image.Width   = imageSize;
                    image.Height  = imageSize;
                    image.Stretch = System.Windows.Media.Stretch.Uniform;
                }

                // The named ShortcutGrid — find it by name, not just first Grid
                var grid = FindVisualChild<Grid>(item, "ShortcutGrid");
                if (grid != null)
                {
                    int cell = containerSize + 20;
                    grid.Width  = cell;
                    grid.Height = cell;
                }
            }
        }

        // ── Data serialization ─────────────────────────────────────────────────
        public FenceData GetFenceData()
        {
            return new FenceData
            {
                Title = FenceTitle,
                X = Left,
                Y = Top,
                Width = Width,
                // FIX 3: Save _expandedHeight so it is restored on next load
                Height = _isCollapsed ? _expandedHeight : Height,
                IsCollapsed = _isCollapsed,
                Opacity = Opacity,
                BackgroundColor = BrushToHex(MainBorder.Background),
                BorderColor = BrushToHex(MainBorder.BorderBrush),
                TitleColor = BrushToHex(TitleBar.Background),
                Mode = Mode,
                IconSize = IconSize,
                IsLocked = _isLocked,
                Shortcuts = Shortcuts
                    .Select(s => new ShortcutData { Name = s.Name, Path = s.Path })
                    .ToList()
            };
        }

        private static string BrushToHex(Brush? brush)
        {
            if (brush is SolidColorBrush scb)
            {
                var c = scb.Color;
                return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            return "#CC1A1A2E";
        }

        // ── Icon loading ───────────────────────────────────────────────────────
        private ImageSource LoadIcon(string path)
        {
            try
            {
                bool isLnk = path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);

                // ── .lnk-first path ────────────────────────────────────────────
                // Steam shortcuts store the real icon in IconLocation (a .ico file).
                // We MUST try this BEFORE IShellItemImageFactory because the Shell
                // sometimes returns a generic/blank bitmap for steam:// shortcuts
                // that passes IsUsableIcon but is not the actual game icon.
                if (isLnk)
                {
                    // ① IconLocation inside the .lnk (Steam stores game .ico here)
                    var sIco = LoadIconFromLnkIconLocation(path);
                    if (IsUsableIcon(sIco, 32)) return sIco!;

                    // ② Resolve the .lnk target exe and try the full chain on it
                    string? target = ResolveLnkTarget(path);
                    if (target != null && File.Exists(target))
                    {
                        var t1 = LoadIconViaShellItemFactory(target, 256);
                        if (IsUsableIcon(t1, 48)) return t1!;

                        var t2 = LoadViaExtractIconEx(target, 0, jumbo: true);
                        if (IsUsableIcon(t2, 64)) return t2!;

                        var t3 = LoadSiblingIco(target);
                        if (IsUsableIcon(t3, 16)) return t3!;

                        var t4 = LoadViaExtractIconEx(target, 0, jumbo: false);
                        if (IsUsableIcon(t4, 16)) return ScaleUpIcon(t4!, 256)!;
                    }

                    // ③ Shell thumbnail on the .lnk itself (last resort for .lnk)
                    var sShell1 = LoadIconViaShellItemFactory(path, 256);
                    if (IsUsableIcon(sShell1, 48)) return sShell1!;

                    var sShell2 = LoadIconViaShellItemFactory(path, 128);
                    if (IsUsableIcon(sShell2, 48)) return sShell2!;

                    var sJumbo = LoadViaExtractIconEx(path, 0, jumbo: true);
                    if (IsUsableIcon(sJumbo, 64)) return sJumbo!;

                    var sFi = LoadViaSHGetFileInfo(path);
                    if (IsUsableIcon(sFi, 16)) return ScaleUpIcon(sFi!, 256)!;

                    return FallbackIcon();
                }

                // ── Normal (non-.lnk) path ─────────────────────────────────────
                // ① Shell thumbnail — best for .exe and most regular files
                var s1 = LoadIconViaShellItemFactory(path, 256);
                if (IsUsableIcon(s1, 48)) return s1!;

                var s2 = LoadIconViaShellItemFactory(path, 128);
                if (IsUsableIcon(s2, 48)) return s2!;

                // ② SHIL_JUMBO system image list (256px)
                var s3 = LoadViaExtractIconEx(path, 0, jumbo: true);
                if (IsUsableIcon(s3, 64)) return s3!;

                // ③ ExtractIconEx large, scaled up
                var s4 = LoadViaExtractIconEx(path, 0, jumbo: false);
                if (IsUsableIcon(s4, 16)) return ScaleUpIcon(s4!, 256)!;

                // ④ SHGetFileInfo fallback
                var s5 = LoadViaSHGetFileInfo(path);
                if (IsUsableIcon(s5, 16)) return ScaleUpIcon(s5!, 256)!;

                // ⑤ Sibling .ico
                var s6 = LoadSiblingIco(path);
                if (IsUsableIcon(s6, 16)) return s6!;
            }
            catch { }

            return FallbackIcon();
        }

        /// <summary>
        /// Reads the IconLocation field stored inside a .lnk file via IShellLink.
        /// Steam shortcuts store their icon as a .ico file path — e.g.
        /// C:\Program Files (x86)\Steam\steam\games\XXXXXXXX.ico
        /// </summary>
        /// <summary>
        /// Reads the IconLocation field from inside a .lnk file via IShellLink.
        /// Steam shortcuts point to a .ico file — e.g.
        /// C:\Program Files (x86)\Steam\steam\games\XXXXXXXX.ico
        /// </summary>
        private static ImageSource? LoadIconFromLnkIconLocation(string lnkPath)
        {
            try
            {
                var shellLink = (IShellLink)new ShellLink();
                var persist   = (IPersistFile)shellLink;
                persist.Load(lnkPath, 0 /* STGM_READ */);

                // 1024-char buffer — some Steam/Epic paths exceed 260 chars
                var iconPathBuf = new System.Text.StringBuilder(1024);
                shellLink.GetIconLocation(iconPathBuf, 1024, out int iconIndex);
                string iconLocation = iconPathBuf.ToString().Trim();

                if (string.IsNullOrWhiteSpace(iconLocation)) return null;

                // Expand environment variables like %SystemRoot%, %ProgramFiles%
                iconLocation = Environment.ExpandEnvironmentVariables(iconLocation);

                if (!File.Exists(iconLocation)) return null;

                // If it's a .ico file, load it directly into a MemoryStream
                // so BitmapImage.EndInit() does not race against a closed FileStream.
                if (iconLocation.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] icoBytes = File.ReadAllBytes(iconLocation);
                    using var ms = new System.IO.MemoryStream(icoBytes);
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.StreamSource    = ms;
                    img.CacheOption     = BitmapCacheOption.OnLoad; // reads into memory before ms disposes
                    img.DecodePixelWidth = 256;
                    img.EndInit();
                    img.Freeze();
                    return img;
                }

                // Otherwise extract icon at the given index from the exe/dll
                IntPtr[] hLarge = new IntPtr[1];
                int extracted = ExtractIconEx(iconLocation, iconIndex, hLarge, null, 1);
                if (extracted > 0 && hLarge[0] != IntPtr.Zero)
                {
                    var bmp = Imaging.CreateBitmapSourceFromHIcon(
                        hLarge[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    DestroyIcon(hLarge[0]);
                    bmp.Freeze();
                    return bmp;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Resolves a .lnk target path via IShellLink.</summary>
        private static string? ResolveLnkTarget(string lnkPath)
        {
            try
            {
                var shellLink = (IShellLink)new ShellLink();
                var persist   = (IPersistFile)shellLink;
                persist.Load(lnkPath, 0);

                var targetBuf = new System.Text.StringBuilder(260);
                shellLink.GetPath(targetBuf, 260, IntPtr.Zero, 0);
                string target = targetBuf.ToString();
                return string.IsNullOrWhiteSpace(target) ? null : target;
            }
            catch { return null; }
        }

        /// <summary>Returns true when src is non-null, large enough, and not a blank/solid bitmap.</summary>
        private static bool IsUsableIcon(ImageSource? src, int minPx)
        {
            if (src == null) return false;
            if (src is not BitmapSource bmp) return true;
            if (bmp.PixelWidth < minPx || bmp.PixelHeight < minPx) return false;

            // Detect blank / fully-transparent / solid-color bitmaps that launchers
            // sometimes return instead of the real icon (e.g. Borderlands, Dying Light 2).
            // Sample a few pixels — if all alpha=0 or all identical, reject.
            try
            {
                int stride = bmp.PixelWidth * 4;
                byte[] pixels = new byte[stride * bmp.PixelHeight];
                bmp.CopyPixels(pixels, stride, 0);

                // Check if completely transparent
                bool allTransparent = true;
                bool allSame = true;
                byte r0 = pixels[0], g0 = pixels[1], b0 = pixels[2], a0 = pixels[3];

                // Sample every 8th pixel for speed
                int step = Math.Max(1, pixels.Length / 4 / 4) * 4; // jump by ~1/4 of pixels
                for (int i = 0; i < pixels.Length - 3; i += step)
                {
                    if (pixels[i + 3] > 10) allTransparent = false;
                    if (pixels[i] != r0 || pixels[i+1] != g0 ||
                        pixels[i+2] != b0 || pixels[i+3] != a0) allSame = false;
                    if (!allTransparent && !allSame) break;
                }

                if (allTransparent || allSame) return false;
            }
            catch { /* if sampling fails, accept the icon */ }

            return true;
        }

        /// <summary>Scale a small icon up to targetPx using WPF render pipeline (HighQuality).</summary>
        private static ImageSource? ScaleUpIcon(ImageSource src, int targetPx)
        {
            try
            {
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                    dc.DrawImage(src, new Rect(0, 0, targetPx, targetPx));
                var bmp = new RenderTargetBitmap(targetPx, targetPx, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(dv);
                bmp.Freeze();
                return bmp;
            }
            catch { return src; }
        }

        private static ImageSource? LoadIconViaShellItemFactory(string path, int size)
        {
            try
            {
                int hr = SHCreateItemFromParsingName(path, IntPtr.Zero,
                    ref IID_IShellItemImageFactory, out IShellItemImageFactory? factory);
                if (hr != 0 || factory == null) return null;

                const int SIIGBF_RESIZETOFIT = 0x0;
                hr = factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_RESIZETOFIT, out IntPtr hBitmap);
                Marshal.ReleaseComObject(factory);
                if (hr != 0 || hBitmap == IntPtr.Zero) return null;

                var src = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                DeleteObject(hBitmap);
                src.Freeze();
                return src;
            }
            catch { return null; }
        }

        private static ImageSource? LoadViaExtractIconEx(string path, int iconIndex, bool jumbo)
        {
            try
            {
                if (jumbo)
                {
                    // SHIL_JUMBO = 0x4 — gives 256×256 when available
                    int hr2 = SHGetImageList(SHIL_JUMBO, ref IID_IImageList, out IImageList? imgList);
                    if (hr2 == 0 && imgList != null)
                    {
                        // Use SHGFI_SYSICONINDEX to get the system image-list index (not the icon handle)
                        SHFILEINFO fi = new();
                        SHGetFileInfo(path, 0, out fi, (uint)Marshal.SizeOf<SHFILEINFO>(),
                            SHGFI_SYSICONINDEX | SHGFI_LARGEICON);
                        if (fi.iIcon >= 0)
                        {
                            int hr3 = imgList.GetIcon(fi.iIcon, ILD_TRANSPARENT, out IntPtr hIcon);
                            if (hr3 == 0 && hIcon != IntPtr.Zero)
                            {
                                var bmp = Imaging.CreateBitmapSourceFromHIcon(
                                    hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                                DestroyIcon(hIcon);
                                bmp.Freeze();
                                return bmp;
                            }
                        }
                    }
                }

                // Regular ExtractIconEx (large = 32px), try up to 3 icon indices
                for (int idx = iconIndex; idx <= iconIndex + 2; idx++)
                {
                    IntPtr[] hLarge = new IntPtr[1];
                    IntPtr[] hSmall = new IntPtr[1];
                    int count = ExtractIconEx(path, idx, hLarge, hSmall, 1);
                    if (count > 0 && hLarge[0] != IntPtr.Zero)
                    {
                        var bmp = Imaging.CreateBitmapSourceFromHIcon(
                            hLarge[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        DestroyIcon(hLarge[0]);
                        if (hSmall[0] != IntPtr.Zero) DestroyIcon(hSmall[0]);
                        bmp.Freeze();
                        return bmp;
                    }
                }
            }
            catch { }
            return null;
        }

        private static ImageSource? LoadViaSHGetFileInfo(string path)
        {
            try
            {
                SHFILEINFO info = new();
                IntPtr hr = SHGetFileInfo(path, 0, out info,
                    (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
                if (hr != IntPtr.Zero && info.hIcon != IntPtr.Zero)
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(
                        info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    DestroyIcon(info.hIcon);
                    return src;
                }
            }
            catch { }
            return null;
        }

        private static ImageSource? LoadSiblingIco(string exePath)
        {
            try
            {
                if (!File.Exists(exePath)) return null;
                string dir  = Path.GetDirectoryName(exePath)!;
                string stem = Path.GetFileNameWithoutExtension(exePath);
                var candidates = new[]
                {
                    Path.Combine(dir, stem + ".ico"),
                    Path.Combine(dir, "icon.ico"),
                    Path.Combine(dir, "app.ico"),
                };
                foreach (var f in candidates)
                {
                    if (!File.Exists(f)) continue;
                    using var stream = File.OpenRead(f);
                    var img = new System.Windows.Media.Imaging.BitmapImage();
                    img.BeginInit();
                    img.StreamSource   = stream;
                    img.CacheOption    = BitmapCacheOption.OnLoad;
                    img.DecodePixelWidth = 256;
                    img.EndInit();
                    img.Freeze();
                    return img;
                }
            }
            catch { }
            return null;
        }

        private static ImageSource FallbackIcon()
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x77)),
                    new Pen(Brushes.SlateGray, 1), new Rect(4, 4, 56, 56));
                dc.DrawText(new FormattedText("?",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 32, Brushes.White,
                    VisualTreeHelper.GetDpi(dv).PixelsPerDip),
                    new Point(22, 12));
            }
            var bmp = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private static void TrySetBrush(Action<Brush> apply, string hex, string fallback,
            byte? overrideAlpha = null)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex)!;
                if (overrideAlpha.HasValue) color.A = overrideAlpha.Value;
                apply(new SolidColorBrush(color));
            }
            catch
            {
                apply(new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)!));
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent, string? name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T fe && (name == null || fe.Name == name)) return fe;
                var found = FindVisualChild<T>(child, name);
                if (found != null) return found;
            }
            return null;
        }

        // ── Native API ─────────────────────────────────────────────────────────
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        private const uint SHGFI_ICON       = 0x100;
        private const uint SHGFI_LARGEICON  = 0x000;
        private const uint SHGFI_SYSICONINDEX = 0x4000;
        private const int  SHIL_JUMBO       = 0x4;
        private const uint ILD_TRANSPARENT  = 0x1;

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string path, uint attr,
            out SHFILEINFO psfi, uint cbfi, uint flags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);

        [DllImport("shell32.dll")]
        private static extern int SHGetImageList(int iImageList, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IImageList? ppv);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int ExtractIconEx(string lpszFile, int nIconIndex,
            IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, int nIcons);

        private static Guid IID_IShellItemImageFactory = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");
        private static Guid IID_IImageList             = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx, cy; }

        [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE sizel, int flags, out IntPtr phbm);
        }

        [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IImageList
        {
            [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, out int pi);
            [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, out int pi);
            [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
            [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
            [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, out int pi);
            [PreserveSig] int Draw(IntPtr pimldp);
            [PreserveSig] int Remove(int i);
            [PreserveSig] int GetIcon(int i, uint flags, out IntPtr picon);
            // (remaining methods omitted — we only need GetIcon)
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("gdi32.dll")]  private static extern bool DeleteObject(IntPtr hObj);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int idx);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int idx, int val);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndAfter, int x, int y, int cx, int cy, uint flags);

        // DWM glass now handled by WindowsIntegrationService

        // ── IShellLink / IPersistFile — for reading .lnk icon location ────────
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLink
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile,
                         int cch, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath,
                                 int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, Guid("0000010B-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            void IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }
    }
}
