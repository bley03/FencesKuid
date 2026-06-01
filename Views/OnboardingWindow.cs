using FencesWPF.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FencesWPF.Views
{
    /// <summary>
    /// First-launch welcome screen.
    /// Shown when the app starts with zero fences.
    /// Built entirely in code — no XAML dependency.
    /// </summary>
    public class OnboardingWindow : Window
    {
        public OnboardingWindow()
        {
            Width  = 460;
            Height = 540;
            WindowStyle           = WindowStyle.None;
            AllowsTransparency    = true;
            Background            = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode            = ResizeMode.NoResize;
            Topmost               = true;

            BuildUI();
            Loaded += (_, _) => PlayEntrance();
        }

        private void BuildUI()
        {
            var root = new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(0x0F, 0x0C, 0x29)),
                CornerRadius    = new CornerRadius(20),
                BorderBrush     = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
            };
            root.Effect = new System.Windows.Media.Effects.DropShadowEffect
            { Color = Colors.Black, BlurRadius = 40, ShadowDepth = 0, Opacity = 0.7 };

            // Drag
            root.MouseLeftButtonDown += (_, e) =>
            { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ── Header gradient ────────────────────────────────────────────────
            var header = new Border
            {
                CornerRadius = new CornerRadius(20, 20, 0, 0),
                Padding      = new Thickness(32, 36, 32, 28),
            };
            header.Background = new LinearGradientBrush(
                Color.FromRgb(0x5B, 0x21, 0xB6),
                Color.FromRgb(0x1D, 0x4E, 0xD8),
                new Point(0, 0), new Point(1, 1));

            var headerContent = new StackPanel();
            headerContent.Children.Add(new TextBlock
            {
                Text               = "✦",
                FontSize           = 36,
                Foreground         = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin             = new Thickness(0, 0, 0, 12),
                Opacity            = 0.9
            });
            headerContent.Children.Add(new TextBlock
            {
                Text               = "Bienvenido",
                FontSize           = 28,
                FontWeight         = FontWeights.Bold,
                Foreground         = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            headerContent.Children.Add(new TextBlock
            {
                Text               = "Tu escritorio, organizado con estilo",
                FontSize           = 13,
                Foreground         = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin             = new Thickness(0, 6, 0, 0)
            });
            header.Child = headerContent;
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            // ── Steps ─────────────────────────────────────────────────────────
            var steps = new StackPanel { Margin = new Thickness(32, 24, 32, 0) };

            steps.Children.Add(MakeStep("1", "Doble clic en el escritorio",
                "Crea un nuevo panel de accesos directos donde quieras."));
            steps.Children.Add(MakeStep("2", "Arrastra tus accesos",
                "Suelta cualquier .lnk, .exe o carpeta dentro del panel."));
            steps.Children.Add(MakeStep("3", "Personaliza a tu gusto",
                "Clic derecho → Configuración para cambiar color, tamaño y modo."));
            steps.Children.Add(MakeStep("4", "Win + Espacio para Peek",
                "Muestra todos tus paneles instantáneamente sobre cualquier ventana."));

            Grid.SetRow(steps, 1);
            grid.Children.Add(steps);

            // ── Footer ─────────────────────────────────────────────────────────
            var footer = new Border
            {
                CornerRadius    = new CornerRadius(0, 0, 20, 20),
                Padding         = new Thickness(32, 20, 32, 24),
                Background      = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
                BorderBrush     = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(0, 1, 0, 0)
            };

            var footerContent = new StackPanel();

            var btnStart = new Border
            {
                CornerRadius    = new CornerRadius(12),
                Padding         = new Thickness(0, 14, 0, 14),
                Cursor          = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            btnStart.Background = new LinearGradientBrush(
                Color.FromRgb(0x7C, 0x3A, 0xED),
                Color.FromRgb(0x2D, 0x6A, 0xFF),
                0);

            var btnText = new TextBlock
            {
                Text               = "Empezar →",
                Foreground         = Brushes.White,
                FontSize           = 14,
                FontWeight         = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            btnStart.Child = btnText;

            btnStart.MouseLeftButtonUp += (_, _) =>
            {
                // Create a starter fence as example
                FenceManager.Instance.CreateDefaultFence();
                PlayExit();
            };
            btnStart.MouseEnter += (_, _) =>
            {
                btnStart.Background = new LinearGradientBrush(
                    Color.FromRgb(0x8B, 0x5C, 0xF6),
                    Color.FromRgb(0x3D, 0x7A, 0xFF), 0);
            };
            btnStart.MouseLeave += (_, _) =>
            {
                btnStart.Background = new LinearGradientBrush(
                    Color.FromRgb(0x7C, 0x3A, 0xED),
                    Color.FromRgb(0x2D, 0x6A, 0xFF), 0);
            };

            footerContent.Children.Add(btnStart);
            footerContent.Children.Add(new TextBlock
            {
                Text               = "También puedes cerrar esta ventana y hacer doble clic en el escritorio",
                FontSize           = 10,
                Foreground         = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping       = TextWrapping.Wrap,
                TextAlignment      = TextAlignment.Center,
                Margin             = new Thickness(0, 10, 0, 0)
            });

            footer.Child = footerContent;
            Grid.SetRow(footer, 2);
            grid.Children.Add(footer);

            root.Child = grid;
            Content    = root;
        }

        private static Grid MakeStep(string number, string title, string description)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Number badge
            var badge = new Border
            {
                Width        = 28, Height = 28,
                CornerRadius = new CornerRadius(8),
                Background   = new SolidColorBrush(Color.FromArgb(0x30, 0xA8, 0x55, 0xF7)),
                BorderBrush  = new SolidColorBrush(Color.FromArgb(0x60, 0xA8, 0x55, 0xF7)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment   = VerticalAlignment.Top,
                Margin       = new Thickness(0, 2, 0, 0)
            };
            badge.Child = new TextBlock
            {
                Text               = number,
                Foreground         = new SolidColorBrush(Color.FromRgb(0xC0, 0x84, 0xFC)),
                FontSize           = 12,
                FontWeight         = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            Grid.SetColumn(badge, 0);
            row.Children.Add(badge);

            // Text
            var textBlock = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            textBlock.Children.Add(new TextBlock
            {
                Text       = title,
                Foreground = Brushes.White,
                FontSize   = 13,
                FontWeight = FontWeights.SemiBold
            });
            textBlock.Children.Add(new TextBlock
            {
                Text         = description,
                Foreground   = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                FontSize     = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 3, 0, 0)
            });
            Grid.SetColumn(textBlock, 1);
            row.Children.Add(textBlock);

            return row;
        }

        // ── Animations ────────────────────────────────────────────────────────
        private void PlayEntrance()
        {
            Opacity = 0;
            Top    += 20;

            var fade = new DoubleAnimation(0, 1,
                new Duration(TimeSpan.FromMilliseconds(400)))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            BeginAnimation(OpacityProperty, fade);

            var slide = new DoubleAnimation(Top, Top - 20,
                new Duration(TimeSpan.FromMilliseconds(400)))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            BeginAnimation(TopProperty, slide);
        }

        private void PlayExit()
        {
            var fade = new DoubleAnimation(1, 0,
                new Duration(TimeSpan.FromMilliseconds(250)))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fade);
        }
    }
}
