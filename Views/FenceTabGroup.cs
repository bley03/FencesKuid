using FencesWPF.Models;
using FencesWPF.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace FencesWPF.Views
{
    /// <summary>
    /// A window that stacks multiple FenceData panels behind a shared tab bar.
    /// Tabs appear at the top of the title area. Clicking a tab switches content.
    /// Dragging a tab out converts it back to a standalone FencePanel.
    /// </summary>
    public class FenceTabGroup : Window
    {
        // ── Data ───────────────────────────────────────────────────────────────
        public TabGroupData GroupData { get; }
        private readonly List<FenceData> _tabs;
        private int _activeIndex = 0;

        // ── UI refs ────────────────────────────────────────────────────────────
        private StackPanel _tabBar       = new();
        private Grid       _contentArea  = new();
        private Border     _mainBorder   = new();
        private TextBlock  _titleText    = new();



        // ── Constants ─────────────────────────────────────────────────────────
        private const double TitleBarHeight = 44;
        private const double TabBarHeight   = 28;

        // ── Native ────────────────────────────────────────────────────────────
        private static readonly IntPtr HWND_BOTTOM  = new(1);
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOMOVE     = 0x0002;
        private const uint SWP_NOSIZE     = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndAfter,
            int x, int y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int idx);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int idx, int val);

        // ── Constructor ───────────────────────────────────────────────────────
        public FenceTabGroup(TabGroupData data)
        {
            GroupData = data;
            _tabs     = data.Tabs;
            _activeIndex = Math.Clamp(data.ActiveTabIndex, 0, Math.Max(0, _tabs.Count - 1));

            Left   = data.X;
            Top    = data.Y;
            Width  = data.Width;
            Height = data.Height;
            Opacity = data.Opacity;

            WindowStyle        = WindowStyle.None;
            AllowsTransparency = true;
            Background         = Brushes.Transparent;
            ShowInTaskbar      = false;
            ResizeMode         = ResizeMode.CanResize;
            MinWidth           = 180;
            MinHeight          = 80;

            BuildUI();

            Loaded  += OnLoaded;
            Closed  += OnClosed;
            SizeChanged      += (_, _) => FenceManager.Instance.SaveLayout();
            LocationChanged  += (_, _) => FenceManager.Instance.SaveLayout();
        }

        // ── Build UI ──────────────────────────────────────────────────────────
        private void BuildUI()
        {
            _mainBorder = new Border
            {
                Background      = new SolidColorBrush(Color.FromArgb(0xD9, 0x0F, 0x0F, 0x1E)),
                BorderBrush     = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(20),
            };
            _mainBorder.Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0xA8, 0x55, 0xF7),
                BlurRadius = 40, ShadowDepth = 0, Opacity = 0.25
            };

            var outerGrid = new Grid();

            // Glass highlight
            outerGrid.Children.Add(new Border
            {
                CornerRadius    = new CornerRadius(20),
                IsHitTestVisible = false,
                Opacity         = 0.04,
                Background      = new LinearGradientBrush(Colors.White, Colors.Transparent,
                                      new Point(0, 0), new Point(1, 1))
            });

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleBarHeight) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TabBarHeight)  });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ── Title bar ─────────────────────────────────────────────────────
            var titleBar = BuildTitleBar();
            Grid.SetRow(titleBar, 0);
            mainGrid.Children.Add(titleBar);

            // ── Tab bar ───────────────────────────────────────────────────────
            var tabBarBorder = BuildTabBar();
            Grid.SetRow(tabBarBorder, 1);
            mainGrid.Children.Add(tabBarBorder);

            // Separator
            var sep = new System.Windows.Shapes.Rectangle
            {
                Height = 1,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible  = false
            };
            var sepBrush = new LinearGradientBrush();
            sepBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0,   0xFF,0xFF,0xFF), 0));
            sepBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x25,0xFF,0xFF,0xFF), 0.5));
            sepBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0,   0xFF,0xFF,0xFF), 1));
            sepBrush.StartPoint = new Point(0,0);
            sepBrush.EndPoint   = new Point(1,0);
            sep.Fill = sepBrush;
            Grid.SetRow(sep, 1);
            mainGrid.Children.Add(sep);

            // ── Content area ──────────────────────────────────────────────────
            _contentArea = new Grid();
            Grid.SetRow(_contentArea, 2);
            mainGrid.Children.Add(_contentArea);

            outerGrid.Children.Add(mainGrid);
            _mainBorder.Child = outerGrid;
            Content = _mainBorder;

            RefreshContent();
        }

        // ── Title bar ─────────────────────────────────────────────────────────
        private Border BuildTitleBar()
        {
            var titleBar = new Border
            {
                CornerRadius = new CornerRadius(20, 20, 0, 0),
                Background   = new LinearGradientBrush(
                    Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF),
                    Color.FromArgb(0x06, 0xFF, 0xFF, 0xFF), 90)
            };
            titleBar.MouseLeftButtonDown += TitleBar_MouseDown;

            var g = new Grid { Margin = new Thickness(14, 0, 8, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Accent dot
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 8, Height = 8,
                Fill  = new SolidColorBrush(Color.FromRgb(0xA8, 0x55, 0xF7)),
                VerticalAlignment = VerticalAlignment.Center
            };
            dot.Effect = new DropShadowEffect
            { Color = Color.FromRgb(0xA8, 0x55, 0xF7), BlurRadius = 10, ShadowDepth = 0 };
            Grid.SetColumn(dot, 0);
            g.Children.Add(dot);

            // Title — shows active tab name
            _titleText = new TextBlock
            {
                Foreground        = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)),
                FontWeight        = FontWeights.SemiBold,
                FontSize          = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming      = TextTrimming.CharacterEllipsis,
                Margin            = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(_titleText, 2);
            g.Children.Add(_titleText);

            // Buttons
            var btnPanel = new StackPanel
            {
                Orientation       = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            btnPanel.Children.Add(MakeTitleBtn("➕", "Nueva pestaña",    () => AddTab()));
            btnPanel.Children.Add(MakeTitleBtn("−",  "Colapsar",         () => { }));
            btnPanel.Children.Add(MakeTitleBtn("⚙",  "Configuración",    () => { }));
            btnPanel.Children.Add(MakeTitleBtn("🗑",  "Eliminar grupo",   DeleteGroup, isDelete: true));

            Grid.SetColumn(btnPanel, 3);
            g.Children.Add(btnPanel);

            titleBar.Child = g;
            return titleBar;
        }

        // ── Tab bar ───────────────────────────────────────────────────────────
        private Border BuildTabBar()
        {
            var container = new Border
            {
                Background      = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(0),
                Padding         = new Thickness(8, 0, 8, 0)
            };

            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
                VerticalAlignment             = VerticalAlignment.Center
            };

            _tabBar = new StackPanel
            {
                Orientation       = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            scroll.Content    = _tabBar;
            container.Child   = scroll;
            return container;
        }

        // ── Rebuild tab buttons ───────────────────────────────────────────────
        private void RefreshTabBar()
        {
            _tabBar.Children.Clear();
            for (int i = 0; i < _tabs.Count; i++)
            {
                int capturedIndex = i;
                var tab = _tabs[i];
                bool isActive = i == _activeIndex;

                var pill = new Border
                {
                    CornerRadius    = new CornerRadius(8),
                    Padding         = new Thickness(10, 3, 8, 3),
                    Margin          = new Thickness(0, 3, 4, 3),
                    Cursor          = Cursors.Hand,
                    Background      = isActive
                        ? new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF))
                        : new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)),
                    BorderBrush     = isActive
                        ? new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF))
                        : Brushes.Transparent,
                    BorderThickness = new Thickness(1)
                };

                var row = new StackPanel { Orientation = Orientation.Horizontal };

                // Tab accent dot (uses tab's accent color)
                var tabDot = new System.Windows.Shapes.Ellipse
                {
                    Width  = 6, Height = 6,
                    Fill   = GetTabAccentBrush(tab),
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                row.Children.Add(tabDot);

                // Tab label
                var label = new TextBlock
                {
                    Text      = tab.Title,
                    Foreground = isActive
                        ? Brushes.White
                        : new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                    FontSize   = 11,
                    FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxWidth   = 100,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                row.Children.Add(label);

                // Close tab button (hidden unless active)
                if (isActive && _tabs.Count > 1)
                {
                    var closeBtn = new TextBlock
                    {
                        Text      = " ×",
                        Foreground = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)),
                        FontSize   = 12,
                        Cursor     = Cursors.Hand,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    closeBtn.MouseLeftButtonUp += (_, e) =>
                    {
                        e.Handled = true;
                        DetachTab(capturedIndex);
                    };
                    closeBtn.MouseEnter += (_, _) =>
                        closeBtn.Foreground = Brushes.White;
                    closeBtn.MouseLeave += (_, _) =>
                        closeBtn.Foreground = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF));
                    row.Children.Add(closeBtn);
                }

                pill.Child = row;

                // Click → switch tab
                pill.MouseLeftButtonUp += (_, e) =>
                {
                    if (e.Handled) return;
                    SwitchTab(capturedIndex);
                };

                // Hover effect
                pill.MouseEnter += (_, _) =>
                {
                    if (capturedIndex != _activeIndex)
                        pill.Background = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
                };
                pill.MouseLeave += (_, _) =>
                {
                    if (capturedIndex != _activeIndex)
                        pill.Background = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
                };

                _tabBar.Children.Add(pill);
            }
        }

        // ── Content area ──────────────────────────────────────────────────────
        private void RefreshContent()
        {
            RefreshTabBar();

            _contentArea.Children.Clear();

            if (_tabs.Count == 0) return;

            var activeTab = _tabs[_activeIndex];
            if (activeTab == null) return;
            _titleText.Text = activeTab.Title;

            // Build a lightweight shortcut grid for the active tab
            var contentPanel = BuildTabContent(activeTab);
            _contentArea.Children.Add(contentPanel);
        }

        private UIElement BuildTabContent(FenceData tab)
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var wrap = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemWidth   = 72,
                ItemHeight  = 96,
                Margin      = new Thickness(6)
            };

            foreach (var sc in tab.Shortcuts ?? Enumerable.Empty<ShortcutData>())
            {
                if (sc == null) continue;
                var icon = new Border
                {
                    Width           = 52, Height = 52,
                    CornerRadius    = new CornerRadius(16),
                    Background      = new LinearGradientBrush(
                        Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF),
                        Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF),
                        new Point(0, 0), new Point(1, 1)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Top,
                    Margin          = new Thickness(0, 2, 0, 0)
                };

                // Load icon
                var img = new Image
                {
                    Width   = 36, Height = 36,
                    Stretch = Stretch.Uniform,
                    
                };
                try
                {
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                img.Source = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri(sc.Path ?? string.Empty, UriKind.Absolute));
                }
                catch { }

                icon.Child = img;

                var nameBlock = new TextBlock
                {
                    Text            = sc.Name,
                    Foreground      = new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF)),
                    FontSize        = 9,
                    TextWrapping    = TextWrapping.Wrap,
                    TextAlignment   = TextAlignment.Center,
                    TextTrimming    = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    MaxHeight       = 24,
                    Width           = 66
                };

                var cell = new StackPanel
                {
                    Width               = 68,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Cursor              = Cursors.Hand
                };
                cell.Children.Add(icon);
                cell.Children.Add(nameBlock);

                cell.MouseLeftButtonUp += (_, _) =>
                {
                    try { System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo
                        { FileName = sc.Path, UseShellExecute = true }); }
                    catch { }
                };

                wrap.Children.Add(cell);
            }

            // Drop hint
            if (tab.Shortcuts.Count == 0)
            {
                var hint = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center
                };
                hint.Children.Add(new TextBlock
                {
                    Text = "⬇", FontSize = 22,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF))
                });
                hint.Children.Add(new TextBlock
                {
                    Text = "Arrastra accesos aquí",
                    Foreground = new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF)),
                    FontSize = 10
                });
                return hint;
            }

            scroll.Content = wrap;
            return scroll;
        }

        // ── Tab switching (with fade animation) ───────────────────────────────
        private void SwitchTab(int index)
        {
            if (index == _activeIndex || index < 0 || index >= _tabs.Count) return;

            // Fade out current
            var fadeOut = new DoubleAnimation(1, 0,
                new Duration(TimeSpan.FromMilliseconds(80)));
            fadeOut.Completed += (_, _) =>
            {
                _activeIndex = index;
                GroupData.ActiveTabIndex = index;
                RefreshContent();

                // Fade in new
                _contentArea.Opacity = 0;
                var fadeIn = new DoubleAnimation(0, 1,
                    new Duration(TimeSpan.FromMilliseconds(120)))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                _contentArea.BeginAnimation(OpacityProperty, fadeIn);
            };
            _contentArea.BeginAnimation(OpacityProperty, fadeOut);
            FenceManager.Instance.SaveLayout();
        }

        // ── Add / remove tabs ─────────────────────────────────────────────────
        public void AddTab(FenceData? data = null)
        {
            var newTab = data ?? new FenceData
            {
                Title     = $"Pestaña {_tabs.Count + 1}",
                Shortcuts = new List<ShortcutData>()
            };
            _tabs.Add(newTab);
            SwitchTab(_tabs.Count - 1);
            FenceManager.Instance.SaveLayout();
        }

        /// <summary>
        /// Removes the tab at index and converts it to a standalone FencePanel.
        /// </summary>
        public void DetachTab(int index)
        {
            if (index < 0 || index >= _tabs.Count) return;

            var tabData = _tabs[index];
            if (tabData == null) return;

            _tabs.RemoveAt(index);
            _activeIndex = Math.Clamp(_activeIndex, 0, Math.Max(0, _tabs.Count - 1));
            GroupData.ActiveTabIndex = _activeIndex;

            // If only one tab left, dissolve the group
            if (_tabs.Count == 1)
            {
                var remaining = _tabs[0];
                if (remaining == null) return;
                _tabs.Clear();
                FenceManager.Instance.DissolveTabGroup(this, remaining);
                return;
            }
            else if (_tabs.Count == 0)
            {
                FenceManager.Instance.RemoveTabGroup(this);
                Close();
                return;
            }

            // Create standalone fence offset from current position
            tabData.X = Left + 30;
            tabData.Y = Top  + 30;
            FenceManager.Instance.CreateFenceFromTabData(tabData);

            RefreshContent();
            FenceManager.Instance.SaveLayout();
        }

        private void DeleteGroup()
        {
            var result = MessageBox.Show(
                $"¿Eliminar este grupo de {_tabs.Count} pestaña(s)?\nSe perderán todos los accesos directos.",
                "Eliminar grupo", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            FenceManager.Instance.RemoveTabGroup(this);
            Close();
        }

        // ── Window chrome ─────────────────────────────────────────────────────
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) return;
            if (e.LeftButton == MouseButtonState.Pressed)
            {
    DragMove();
            }
        }

        // ── Loaded ────────────────────────────────────────────────────────────
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;

            int ex = GetWindowLong(hwnd, -20); // GWL_EXSTYLE
            ex |= 0x00000080 | 0x08000000;    // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
            SetWindowLong(hwnd, -20, ex);

            WindowsIntegrationService.ApplyGlassEffect(hwnd);
            WindowsIntegrationService.ApplyDarkTitleBar(hwnd, true);
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

            // Entrance animation
            Opacity = 0;
            var fade = new DoubleAnimation(0, GroupData.Opacity,
                new Duration(TimeSpan.FromMilliseconds(350)))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
              BeginTime      = TimeSpan.FromMilliseconds(80) };
            BeginAnimation(OpacityProperty, fade);
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            FenceManager.Instance.RemoveTabGroup(this);
        }

        // ── Serialization ─────────────────────────────────────────────────────
        public TabGroupData GetGroupData()
        {
            GroupData.X              = Left;
            GroupData.Y              = Top;
            GroupData.Width          = Width;
            GroupData.Height         = Height;
            GroupData.Opacity        = Opacity;
            GroupData.ActiveTabIndex = _activeIndex;
            GroupData.Tabs           = _tabs;
            return GroupData;
        }

        // ── Peek support ──────────────────────────────────────────────────────
        public void PeekShow(Duration fadeIn)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            var anim = new DoubleAnimation(Opacity, 1.0, fadeIn)
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            BeginAnimation(OpacityProperty, anim);
        }

        public void PeekHide(Duration fadeOut)
        {
            var hwnd   = new WindowInteropHelper(this).Handle;
            double target = GroupData.Opacity;
            var anim = new DoubleAnimation(1.0, target, fadeOut)
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            anim.Completed += (_, _) =>
            {
                SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                BeginAnimation(OpacityProperty, null);
                Opacity = target;
            };
            BeginAnimation(OpacityProperty, anim);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static SolidColorBrush GetTabAccentBrush(FenceData tab)
        {
            try
            {
                var raw = ColorConverter.ConvertFromString(tab.BorderColor);
                if (raw is Color c) return new SolidColorBrush(c);
            }
            catch { }
            return new SolidColorBrush(Color.FromRgb(0xA8, 0x55, 0xF7));
        }

        private static Button MakeTitleBtn(string content, string tooltip,
            Action onClick, bool isDelete = false) => new Button
        {
            Content         = content,
            Width           = 24, Height = 24,
            Margin          = new Thickness(1, 0, 0, 0),
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground      = isDelete
                ? new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0x6B, 0x6B))
                : new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)),
            Cursor          = Cursors.Hand,
            ToolTip         = tooltip,
            Command         = new RelayCommand(onClick)
        };


    }

    // ── Minimal ICommand implementation ───────────────────────────────────────
    internal class RelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action _execute;
        public RelayCommand(Action execute) => _execute = execute;

        // CanExecuteChanged is required by ICommand but never fires — always enabled
        public event EventHandler? CanExecuteChanged
        {
            add    { }
            remove { }
        }

        public bool CanExecute(object? p) => true;
        public void Execute(object? p)    => _execute();
    }
}
